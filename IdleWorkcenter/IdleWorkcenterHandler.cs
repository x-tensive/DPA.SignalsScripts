using DPA.Core.Contracts;
using DPA.Core.DependencyInjection;
using DPA.Core.Services;
using DPA.Messenger.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xtensive.DPA.Contracts;
using Xtensive.DPA.Host.Localization;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;
using Xtensive.Project109.Host.Security;
using Microsoft.Extensions.Logging;

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
		public static readonly string[] USERS = new string[] { };
		/// <summary>
		/// Message teamplate to be used for user notification
		/// </summary>
		private static readonly TimeSpan MinimumRepeatInterval = TimeSpan.FromMinutes(15);
		private const string TEMPLATE_NAME = "WORK CENTER STATE NOTIFICATION";
		private const string HTTP_PROTOCOL = "https";
		private const string HOST = "localhost";
		private const string ONE_TIME_MESSAGE = "WORK CENTER STATE NOTIFICATION";
		private const string MASTER_MESSAGE_SOURCE = "WORK CENTER STATE NOTIFICATION FOR MASTER";
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
			private readonly ILogger logger;
			private readonly IMicroserviceClient microserviceClient;
			private readonly IJobService jobService;
			private readonly MasterRegistrationService masterRegistrationService;
			private readonly IDateTimeOffsetProvider timeProvider;
			private readonly NotificationMessageTaskBuilder messageBuilder;
			private readonly IMicroserviceSettingsService microserviceSettings;

			public BtkInternalHandler(
				IMicroserviceSettingsService microserviceSettings,
				NotificationMessageTaskBuilder messageBuilder,
				IMicroserviceClient microserviceClient,
				IJobService jobService,
				MasterRegistrationService masterRegistrationService,
				IDateTimeOffsetProvider timeProvider,
				ILogger logger)
			{
				this.microserviceSettings = microserviceSettings;
				this.messageBuilder = messageBuilder;
				this.microserviceClient = microserviceClient;
				this.jobService = jobService;
				this.masterRegistrationService = masterRegistrationService;
				this.timeProvider = timeProvider;
				this.logger = logger;
			}

			private void SendMessage(ProductionJob activeJob, Equipment equipment, DateTimeOffset lastEventTimeStamp, string messageSource, params User[] currentRecipients)
			{
				if (currentRecipients == null || currentRecipients.Length == 0) {
					return;
				}

				var template = Query.All<MessageTemplate>().Where(x => x.Name.ToLower() == TEMPLATE_NAME.ToLower()).Single();
				var currentState = Query.All<EquipmentStateRecord>()
					.Where(x => x.Equipment.Id == equipment.Id)
					.OrderByDescending(x => x.StartDate)
					.FirstOrDefault();
				var currentStateValue = MachineStateType.Undefined;

				if (currentState != null) {
					var stateDuration = (currentState.StartDate - timeProvider.Now).Duration();
					if (stateDuration < MinimumRepeatInterval) {
						logger.Info(string.Format("Equipment({0}) state(currently is {1}) has changed {2} seconds ago. It's too early for notification. State should be the same at least for {3} seconds before notification",
							equipment.Name,
							currentStateValue,
							(int)stateDuration.TotalSeconds,
							(int)MinimumRepeatInterval.TotalSeconds)
							);
						return;
					}
					currentStateValue = currentState.Type;
				}

				var parameters = new Dictionary<string, string> {
					{ "EquipmentName", equipment.Name },
					{ "EquipmentId", equipment.Id.ToString() },
					{ "JobName", activeJob.Order },
					{ "JobLink", string.Format("{0}://{1}/operatorNew/#/master/equipment/{2}/tasks/{3}", HTTP_PROTOCOL, HOST, equipment.Id, activeJob.Id )},
					{ "SignalTimestamp", timeProvider.Now.ToString(LocalizationManager.Ru) },
					{ "CurrentEquipmentState", currentStateValue.GetName() },
					{ "LastEventTimeStamp", lastEventTimeStamp == DateTimeOffset.MinValue ? "неизвестного момента": lastEventTimeStamp.ToString(LocalizationManager.Ru)}
				};
				messageBuilder.BuildAndScheduleMessages(
					MessageTransportType.Email,
					template,
					currentRecipients,
					() => parameters,
					() => Array.Empty<Attachment>(),
					(x) => { },
					equipment.DriverIdentifier,
					messageSource
				);
			}

			private async Task SendOneTimeMessage(ProductionJob job, Equipment equipment, DateTimeOffset lastEventTime)
			{
				var existingMessage = await QueryAsync<EntityReference>("message", BuildMessageQuery(equipment.DriverIdentifier, ONE_TIME_MESSAGE, lastEventTime));
				if (existingMessage != null) {
					var existingMessageDelivery = await QueryAsync<MessageDelivery>("messageDelivery", BuildMessageDeliveryQuery(existingMessage.Id));
					if (existingMessageDelivery != null) {
						logger.Info(string.Format("One time notification about not responding driver was already sended. Message [{0}] was queued at [{1}]", existingMessage.Id, existingMessageDelivery.QueuedTime));
						return;
					}
				}

				var oneTimeRecipients = Query.All<DpaUser>()
					.Where(x => USERS.Contains(x.PersonnelNumber))
					.ToArray();

				SendMessage(job, equipment, lastEventTime, ONE_TIME_MESSAGE, oneTimeRecipients);
				logger.Info(string.Format("One time notification about not responding driver was sended for {0} recipients", oneTimeRecipients.Length));
			}

			private async Task SendMessageForMaster(ProductionJob activeJob, Equipment equipment, DateTimeOffset lastEventTime)
			{
				var previousMessageTimestamp = DateTimeOffset.MinValue;
				var previousMessage = await QueryAsync<EntityReference>("message", BuildMessageQuery(equipment.DriverIdentifier, MASTER_MESSAGE_SOURCE, lastEventTime));
				if (previousMessage != null) {
					var messageDelivery = await QueryAsync<MessageDelivery>("messageDelivery", BuildMessageDeliveryQuery(previousMessage.Id));
					if (messageDelivery != null) {
						previousMessageTimestamp = messageDelivery.QueuedTime;
					}
				}

				if ((previousMessageTimestamp - timeProvider.Now).Duration() <= MinimumRepeatInterval) {
					logger.Info(string.Format("It's to early to send new notification for master for equipment '{0}'(driver {1}). Previous message was queued in {2}", equipment.Name, equipment.DriverIdentifier, previousMessageTimestamp));
					return;
				}
				var master = masterRegistrationService.GetCurrentMaster(equipment.Id);
				if (master == null) {
					logger.Info(string.Format("Unable to find current master for sending notification for equipment '{0}'(driver {1})", equipment.Name, equipment.DriverIdentifier));
					return;
				}
				SendMessage(activeJob, equipment, lastEventTime, MASTER_MESSAGE_SOURCE, Query.Single<User>(master.Id));
				logger.Info(string.Format("Notification for master for equipment '{0}'(driver {1}) was sended", equipment.Name, equipment.DriverIdentifier));
			}

			public async Task ExecuteAsync(Guid driverId, DateTimeOffset lastEventTime)
			{
				var equipment = Query.All<Equipment>().Where(x => x.DriverIdentifier == driverId).Single();
				var job = jobService.GetActiveProduction(equipment);
				if (job == null) {
					logger.Info(string.Format("Job was already completed. Notification for equipment '{{0}}'(driver {1}) is cancelled", equipment.Name, driverId));
					return;
				}

				await SendOneTimeMessage(job, equipment, lastEventTime);
				await SendMessageForMaster(job, equipment, lastEventTime);
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

			private static string BuildMessageQuery(Guid driverId, string source, DateTimeOffset lastEventTime)
			{
				var metadata = JsonConvert.SerializeObject(driverId);
				return string.Format(
					"$filter=Source eq '{0}' and Metadata eq '{1}' and CreateTime gt {2}&$orderby=CreateTime desc&$top=1&$select=Id",
					source,
					metadata,
					lastEventTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mmZ")
				);
			}

			private static string BuildMessageDeliveryQuery(long messageId)
			{
				return string.Format("$filter=MessageId eq {0} and (DeliveryStatus eq 'Queued' or DeliveryStatus eq 'Delivered')&orderby=Id&$top=1&$select=Id,QueuedTime", messageId);
			}
		}

		private readonly IInScopeExecutor<IServiceProvider> executor;
		private readonly ILogger<IdleWorkcenterHandler> logger;

		public IdleWorkcenterHandler(IInScopeExecutor<IServiceProvider> executor, ILogger<IdleWorkcenterHandler> logger)
		{
			this.executor = executor;
			this.logger = logger;
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			var signalInfo = (Tuple<Guid, DateTimeOffset>)args.Obj;

			return executor
				.ExecuteAsync(x => {
					var internalHandler = new BtkInternalHandler(
						x.GetRequiredService<IMicroserviceSettingsService>(),
						x.GetRequiredService<NotificationMessageTaskBuilder>(),
						x.GetRequiredService<IMicroserviceClient>(),
						x.GetRequiredService<IJobService>(),
						x.GetRequiredService<MasterRegistrationService>(),
						x.GetRequiredService<IDateTimeOffsetProvider>(),
						logger
					);
					using (new DPALocalizationScope(LocalizationManager.Ru)) {
						return internalHandler.ExecuteAsync(signalInfo.Item1, signalInfo.Item2);
					}
				});
		}
	}
}
