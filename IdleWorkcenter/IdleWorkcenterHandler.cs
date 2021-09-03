using DPA.Core.Contracts;
using DPA.Core.DependencyInjection;
using DPA.Messenger.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;
using Xtensive.Project109.Host.Security;

namespace Xtensive.Project109.Host.DPA
{
	/// <summary>
	/// Will send an email notification to {USER_ID} using the {TEMPLATE_ID} template
	/// Notification will include idle workcenter information
	/// </summary>
	public class IdleWorkcenterHandler : Signals2HandlerBase
	{
		/// <summary>
		/// User to be notified with message about idle driver
		/// </summary>
		public long[] USERS = new long[] { 821 };

		/// <summary>
		/// Message teamplate to be used for user notification
		/// </summary>
		public long TEMPLATE_ID = 325379;
		private const string MESSAGE_SOURCE = "WORK CENTER STATE NOTIFICATION";
		private class Response<T>
		{
			public T Value { get; set; }
		}
		private class MessageDelivery
		{
			public long Id { get; set; }
			public DateTimeOffset QueuedTime { get; set; }
		}
		private class BtkInternalHandler
		{
			private readonly IHostLog logger;
			private readonly IMicroserviceClient microserviceClient;
			private readonly NotificationMessageTaskBuilder messageBuilder;
			private readonly IMicroserviceSettingsService microserviceSettings;

			public BtkInternalHandler(
				IMicroserviceSettingsService microserviceSettings,
				NotificationMessageTaskBuilder messageBuilder,
				IMicroserviceClient microserviceClient,
				IHostLog logger)
			{
				this.microserviceSettings = microserviceSettings;
				this.messageBuilder = messageBuilder;
				this.microserviceClient = microserviceClient;
				this.logger = logger;
			}

			public async Task ExecuteAsync(Guid driverId, DateTimeOffset lastEventTime, long[] userIdList, long templateId)
			{
				if (await ShouldSendMessage(driverId, lastEventTime)) {
					logger.Info(string.Format("Notifying about not responding driver [{0}]", driverId));

					SendMessage(driverId, userIdList, templateId);

					logger.Info(string.Format("Notification about not responding driver [{0}] is done", driverId));
				}
			}

			private async Task<T> QueryAsync<T>(string targetEntity, string query)
			{
				var requestAsString = string.Empty;
				var responseAsString = string.Empty;
				try {
					Uri uri;
					if (!microserviceSettings.TryGetFirstMicroserviceUri(AvailableMicroservices.Messenger, out uri)) {
						throw new InvalidOperationException(string.Format("Unable to find url for microservice [{0}]", AvailableMicroservices.Messenger));
					}
					var uriBuilder = new UriBuilder(uri) {
						Path = "api/" + targetEntity,
						Query = query
					};
					requestAsString = uri.ToString();
					var request = new HttpRequestMessage {
						Method = HttpMethod.Get,
						RequestUri = uriBuilder.Uri
					};
					var user = new MicroserviceUserInfo(User.SystemUser);
					var response = await microserviceClient.SendAsync(request, user, CancellationToken.None);
					responseAsString = await response.Content.ReadAsStringAsync();
					response.EnsureSuccessStatusCode();

					var result = JsonConvert.DeserializeObject<Response<T[]>>(responseAsString);
					return result.Value.SingleOrDefault();
				}
				catch {
					var msg = string.Format("\n\tNot responding driver message request: {0}\n\tNot responding driver message response: {1}", requestAsString, responseAsString);
					logger.Debug(msg);
					throw;
				}
			}

			private static string BuildMessageQuery(Guid driverId, DateTimeOffset lastEventTime)
			{
				var metadata = JsonConvert.SerializeObject(driverId);
				return string.Format(
					"$filter=Source eq '{0}' and Metadata eq '{1}' and CreateTime gt {2}&$orderby=CreateTime desc&$top=1&$select=Id",
					MESSAGE_SOURCE,
					metadata,
					lastEventTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mmZ")
				);
			}

			private static string BuildMessageDeliveryQuery(long messageId)
			{
				return string.Format("$filter=MessageId eq {0} and (DeliveryStatus eq 'Queued' or DeliveryStatus eq 'Delivered')&orderby=Id&$top=1&$select=Id,QueuedTime", messageId);
			}

			private async Task<bool> ShouldSendMessage(Guid driverId, DateTimeOffset lastEventTime)
			{
				var existingMessage = await QueryAsync<EntityReference>("message", BuildMessageQuery(driverId, lastEventTime));
				if (existingMessage == null) {
					return true;
				}

				var messageDelivery = await QueryAsync<MessageDelivery>("messageDelivery", BuildMessageDeliveryQuery(existingMessage.Id));
				if (messageDelivery != null) {
					logger.Info(string.Format("Notification about not responding driver was already sended. Message [{0}] was queued at [{1}]", existingMessage.Id, messageDelivery.QueuedTime));
				}

				return messageDelivery == null;
			}

			private void SendMessage(Guid driverId, long[] usersIdList, long templateId)
			{
				var equipment = Query.All<Equipment>().Where(x => x.DriverIdentifier == driverId).First();
				var template = Query.SingleOrDefault<MessageTemplate>(templateId);
				var users = Query.All<User>().Where(x => usersIdList.Contains(x.Id)).ToArray();
				var parameters = new Dictionary<string, string> {
					{"EquipmentName", equipment.Name },
					{"EquipmentId", equipment.Id.ToString() }
				};
				messageBuilder.BuildAndScheduleMessages(
					MessageTransportType.Email,
					template,
					users,
					() => parameters,
					() => Array.Empty<Attachment>(),
					(x) => { },
					driverId,
					MESSAGE_SOURCE
				);
			}
		}

		private readonly IInScopeExecutor<IServiceProvider> executor;
		private readonly IHostLog<IdleWorkcenterHandler> logger;

		public IdleWorkcenterHandler(IInScopeExecutor<IServiceProvider> executor, IHostLog<IdleWorkcenterHandler> logger)
		{
			this.executor = executor;
			this.logger = logger;
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			var signalInfo = (Tuple<Guid, DateTimeOffset>)args.Obj;

			return executor.ExecuteAsync(x => {
				var internalHandler = new BtkInternalHandler(
					x.GetRequiredService<IMicroserviceSettingsService>(),
					x.GetRequiredService<NotificationMessageTaskBuilder>(),
					x.GetRequiredService<IMicroserviceClient>(),
					logger
				);
				return internalHandler.ExecuteAsync(signalInfo.Item1, signalInfo.Item2, USERS, TEMPLATE_ID);
			});
		}
	}
}
