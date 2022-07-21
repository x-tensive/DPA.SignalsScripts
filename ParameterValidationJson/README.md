## Parameter Validation Json
The script starts the validation of parameter sets on the event of the appearance of a new valid JSON file with data in the specified folder for input data.

To make it work you need to follow the steps:

**1.** Open DPA web application in your browser and navigate to Signals 2.0 from the main menu.

**2.** Go to Common Scripts tab. Here you will need to add some common scripts described below.

**2.1.** ParameterValidationJsonConfig.cs
You can find this file in the folder where the file of this tutorial is located.
There are 3 main settings in this file:
 - INPUT_DIRECTORY - folder for input JSON parameters
 - OUTPUT_DIRECTORY - folder for storing validation results
 - VALIDATION_CHANNEL - work center channel number

Add the file to Common Scripts and change the settings according to your environment.

**2.2.** DatabaseAdapter.cs
This file is required to write data about validation results to the database.
You can find it in the EventsToDatabase folder nearby.
It does not require any configuration, so just add it to your Common Scripts.

**2.3.** EventsToDatabaseSensitiveConfig.cs
This file contains the connection string to the database where the validation results will be written.
Place this file also in Common Scripts and change the connection string value (TARGET_DATABASE_CONNECTION) according to your environment.

**3.** Go to the directory specified in INPUT_DIRECTORY (step 2.1) and create a new folder there whose name will be the same as the name of the work center in the DPA system.

**4.**  Go to Handlers tab.
You can find the handler and trigger files in the same folder as this tutorial.
Find and add ParameterValidationJsonHandler.cs to handlers as is.

**5.** Go to Triggers tab.
Find and add ParameterValidationJsonTrigger.cs to triggers.
Be sure to specify the handle created in the previous step when creating the trigger.

**6.** Compile and run all files starting with common scripts.

**7.** All is ready. To run validation, place the JSON file with data in the folder of the corresponding work center in INPUT_DIRECTORY.
