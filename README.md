# Travic CI Build Status
[![Build Status](https://travis-ci.org/aliozgur/BlackboardMiddleware.svg?branch=master)](https://travis-ci.org/aliozgur/BlackboardMiddleware)

# Blackboard Learn Flat File Upload Middleware
This tool is intended to automate the upload of  [snapshot flat files](https://en-us.help.blackboard.com/Learn/Administrator/Hosting/System_Integration/SIS/SIS_Integration_Types/010_Snapshot_Flat_File) 
to Blackboard.

You can run this tool as a console application or install the tool as a Windows Service.

# Install As Windows Service

1. Run Windows Command Propmpt (cmd.exe) as **Administrator**
2. Run the following command to install
    ```
    Bilgi.Sis.BbMiddleware.exe install
    ```
3. And start the service with the following command
    ```
    Bilgi.Sis.BbMiddleware.exe start
    ```
# Uninstalling the Windows Service

1. Run Windows Command Propmpt (cmd.exe) as **Administrator**
2. Stop the service

    ```
    Bilgi.Sis.BbMiddleware.exe stop
    ```
3. Uninstall the service
    ```
    Bilgi.Sis.BbMiddleware.exe uninstall
    ```
> You can also use ServiceInstall.bat and ServiceUninstall.bat included in the distribution

# File Naming and Ordering
Your generated snapshot flat files should follow this convention
```{BatchId}{BatchSeperator}{FileName}```

**BatchId**: BatchId is intended for grouping batch of snapshot flat files. The tool will always group files based on this value and process one batch at a time.
BatchId should be an incremental value which can be sorted. For example integer values like 1,2,3 are good candidate, you can also use a sequence generator.

**BatchSeperator** : A charcater to seperate the BatchId from the actual file name. Underscore (_) and dash (-) characters are good candidates for batch seperator

**FileName** : The actual file name with file extension. The file name should represent the Blackboard entities. Courses.txt, Users.txt, Enrollments.txt are good candidates for file name.

## Sample Snapshot File Names
* Files in batch 1 : **1_Users.txt**, **1_Nodes.txt**, **1_Courses.txt**
* Files in batch 2 : **2_Users.txt**, **2_Nodes.txt**, **2_Courses.txt**
 
> **Important Notes**
> * The tool will process all files in a batch in a single run. 
> * If a file upload fails in a batch the tool will abort processing of the remaining files. If the error is persistent the failing batch and all subsequent 
batches will not be processed

# Service Configuration
You should configure a ```Bilgi.Sis.BbMiddleware.exe.config``` in the same folders as ```Bilgi.Sis.BbMiddleware.exe``` executable 

## Valid Properties

| Property | Description | Mandatory |
| ---      | ---         | ---       |
| ServiceName | Name of the Windows Service  |  YES     |
| DisplayName | Display name of the Windows Service  |  YES     |
| Description | Descritiion about the service  |  NO     |
|  |  |  |

## Sample Values 
```xml
  <appSettings>
    <add key="ServiceName" value="Bilgi.Sis.BbMiddleware" />
    <add key="DisplayName" value="Bilgi Sis-Bb Data Middleware" />
    <add key="Description" value="Post SIS data to BbLearn" />
  </appSettings>
```

# Data Upload Configuration
You should configure a ```config.json``` in the same folders as ```Bilgi.Sis.BbMiddleware.exe``` executable 


**Batch Data Definition Properties**

| Property | Description | Mandatory |
| ---      | ---         | ---       |
| DryRun | Do not uplaod files to Blackboard but run the full cycle | NO |
| UploadTimeoutInSecs | HTTP client timeout for data file upload. **Default** is **180 seconds**. **Minimum** value can be **100 seconds**  | NO |
| DownloadTimeoutInSecs | HTTP client timeout for status file download. **Default** is **180 seconds**. **Minimum** value can be **100 seconds**   | NO |
| DataIntervalInSeconds | How often will data job trigger. Default is 1 hour (3600 seconds) | YES |
| DataJobEnabled | If 'true' data job will be triggered every DataIntervalInSeconds, if 'false' data job will not be triggered | YES|
| DataRootPath | Full path of the root snapshot flat file folder | YES |
| QueueFolderName | Queued files folder name relative to **DataRootPath** | YES |
| LogFolderName | Log files folder name relative to **DataRootPath** | YES |
| LogEnabled | If 'true' data set post result will be logged into Log Folder, if 'false' data set post result will be ignored | YES |
| ProcessedFolderName | Processed files folder name relative to **DataRootPath** | YES |
| ProcessedBackupEnabled | If 'true' processd data files will be moved to Processed folder, if 'false' data files will be deleted immediately | YES |
| FilePartsSeperator | Seperator character which seperates BatchId value from File Name | YES |
| Username | Blackboard shared username. If no username is specified in Endpoint configuration this value is used | NO |
| Password | Blackboard shared password. If no password is specified in Endpoint configuration this value is used | NO |
| EndpointUrl | Blackboard snaposhot flat file upload endpoint root url. Enpoint url is concatanated to this value if enpoint url does not start with **http** or **https**| NO |
| DataSetStatusJobEnabled | If 'true' data set status job is enabled. Data set status will be queried based on reference id values found in log files| YES |
| DataSetStatusQueryIntervalInSeconds| How often will data set status job trigger. Default is 1.5 hours (5400 seconds)  | YES |
| DataSetStatusMaxFilesToProcess | Maximum number of log files to be processed every time data set status job is triggered| YES |
| DataSetStatusFolderName | The name of the folder where errorneous data transfers will be logged | YES |
| DataSetStatusEndpointUrl | Data set status query enpoint url | YES |
| Endpoints | Array of endpoint  definitions | YES |
|  |  |  |


**Endpoint Definition Properties** 

| Property | Description | Mandatory |
| ---      | ---         | ---       |
| Name | Endpoint name. Used for logging purposes  | YES |
| Order | Used to determine the upload order of the snapshot file to the enpoint. | YES |
| Username | Blackboard shared username. If username is not specified  **DefaultUsername** will be used | NO |
| Password | Blackboard shared password. If username is not specified  **DefaultPassword** will be used | NO |
| Url | Endpoint url. Can be full url or relative path to **RootEndpointUrl** | YES |
| FileName | Name of the snapshot file with file extension. This value is used to match the snapshot file with the endpoint| YES |
| ProcessedFolderName | The name of the folder where processed files will be moved. Relative to **ProcessedFolderName** in the batch definition | NO |
| ProcessedBackupEnabled | If 'true' processd data files will be moved to Processed folder, if 'false' data files will be deleted immediately | YES |
| LogFolderName | The name of the folder where log files will be moved. Relative to **LogFolderName** in the batch definition | NO |
| LogEnabled | If 'true' data set post result will be logged into Log Folder, if 'false' data set post result will be ignored | YES |
|  |  |  |

## Sample JSON File
```json
{
  "DataRootPath": "C:\\BbData\\Test\\Hybridity\\",
  "QueueFolderName": "Queue",
  "ProcessedFolderName": "Processed",
  "ProcessedBackupEnabled": false,
  "LogFolderName": "Logs",
  "LogEnabled": true,
  "FilePartsSeperator": "_", // Default is _ (underscore)
  "Username": "",
  "Password": "password",
  "DataIntervalInSeconds": 10, //Default is 1 hour
  "DataJobEnabled": true, // Default is true
  "EndpointUrl": "https://xyx.com/webapps/bb-data-integration-flatfile-BBLEARN/endpoint",
  "DataSetStatusMaxFilesToProcess": 5, // Default is 5
  "DataSetStatusQueryIntervalInSeconds": 15, // Default is 1.5 hours
  "DataSetStatusJobEnabled": true, // Default is false
  "DataSetStatusFolderName": "Status", // Default is Status
  "DataSetStatusEndpointUrl": "https://xyx.com/webapps/bb-data-integration-flatfile-BBLEARN/endpoint/dataSetStatus/",
  "Endpoints": [
    {
      "Name": "Nodes",
      "Order": 1,
      "Username": "0d7173bd-a1f9-5b99-853f-8768f1b6c1df",
      "Password": "",
      "Url": "/node/store",
      "FileName": "Nodes.txt",
      "ProcessedFolderName": "Nodes",
      "LogFolderName": "Nodes"
    },
    {
      "Name": "Users",
      "Order": 2,
      "Username": "8bd7a8a6-3ee1-43c2-b8ae-403208842922",
      "Password": "",
      "Url": "/person/store",
      "FileName": "Users.txt",
      "ProcessedFolderName": "Users",
      "LogFolderName": "Users"
    },
    {
      "Name": "Courses",
      "Order": 3,
      "Username": "0d499b16-2984-4780-8277-635ea1eb454c",
      "Password": "",
      "Url": "/course/store",
      "FileName": "Courses.txt",
      "ProcessedFolderName": "Courses",
      "LogFolderName": "Courses"
    },
    {
      "Name": "Enrollments",
      "Order": 4,
      "Username": "96838380-070b-5527-afa0-dab324faf848",
      "Password": "",
      "Url": "/membership/store",
      "FileName": "Enrollments.txt",
      "ProcessedFolderName": "Enrollments",
      "LogFolderName": "Enrollments"
    }
  ]
}
```

## Contributors

* Ali Özgür (ali.ozgur@bilgi.edu.tr) -  İstanbul Bilgi University

## License
**The MIT License (MIT)**

Copyright (c) 2016 Ali Özgür

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
