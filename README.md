# perfmon-infra-plugin

Windows Perfmon/WMI Plugin for New Relic Infrastructure
=======================================================

This is an executable that provides Windows Perfmon/WMI query & event results to stdout, in a form that is consumable by New Relic Infrastructure when run as a plugin to it.

### Requirements

* .NET >= 3.5
* New Relic account

### Execution & Command-line Arguments

If run at command line without anything, the executible should report JSON results from WMI queries specified in `config.json` to stdout, and any error messages to stderr.

* `-c | --configFile [file]`: Config file to use (default: `config.json`)
* `-i | --pollInt [nnn]`: Frequency of polling (ms) (default: 10000ms, ignored if less than 10000ms)
* `-n | --compName [name]`: Name of computer that you want to poll (default: local host)
* `-v | --verbose [nnn]`: Verbose logging mode (default: 0, 1 or more enables Verbose)

### Configuration

Out-of-the-box, we have collected a set of Perfmon counters that pertain to .NET applications. If you would like to collect your own counters, customize the `counterlist` in `config.json` following the structure found there. Here is an excerpt describing the format:

#### `config.json` Format

```javascript
{
	"counterlist": [
		{
			"provider": "provider_name",
			"category": "category_name",
			"instance": "(optional) instance_name",
			"counters": [
				{
            		"counter": "*|counter_name"
        		},
        		{
        			"counter": "another_counter_name"
        		}
            ]
		},
		{
			"query": "the_whole_WMI_Query",
			"eventname": "(optional, default: 'WMIQueryResult') insights_event_name",
			"querytype": "(optional, default: 'wmi_query') wmi_query|wmi_eventlistener",
			"(optional) counters": [
				{
            		"counter": "counter_name|counter_class.counter_name",
            		"attrname": "(optional) attribute_name_in_insights_event"
        		},
        		{
        			"counter": "another_counter_name"
        		}
        	]
		}
	]
}
```

#### Simple Queries & Performance Counters

The "provider, category, (optional) instance" form of the counter is for building simple queries, with the following limititions:
  * Uses the default namespace ("root/cimv2")
  * Limited to Select statements against classes with the name `Win32_PerfFormattedData_{provider}_{category}`
  * No custom names for individual attributes
  * Uses "WMIQueryResult" Insights event type.

Notes:
  * The `instance` property is optional. If you leave it out, all instances will be polled automatically.
  * You must have at least one `counter` specified in `counters`. You can use wildcard ('*') as the value to get all counters for that class.
  * If you specify the `provider` as `PerfCounter`, the plugin will retrieve the Windows Performance Counter instead of running a WMI query. This can be useful if WMI is returning "all 0's" in a query or the appropriate Performance Counter is easier to find.   * Example of usage: 
    ```javascript
    {
        "provider": "PerfCounter",
        "category": "ASP.NET v4.0.30319",
        "counters": [{
            "counter": "Requests Current"
        }]
    }
    ```

#### Complex Queries & Event Listeners

For more complex queries, use the "query, eventname, (optional) querytype, (optional) counters" form.
* `querytype` should only be used if you're going to run an event listener instead of a typical WMI Query (set to `wmi_eventlistener`) Note: This listener will operate as a separate thread, so that it doesn't impede other queries from running.
* `eventtype` is optional and will set that query's result events in Insights to anything specified here.
* `counters` is optional here, used to specify counters to extract from the query. In particular, use this when you want to either set a custom attribute name, or retrieve a sub-property from a counter object. Otherwise, you can specify counters in the query itself (i.e. "`Select Name, Description, DeviceID from Win32_PNPEntity`).
  * If you leave out `counters`, all returned counters for that query will be reported as simple name/value pairs and will be named with their original counter name.
  * `attrname` property in `counters` is optional. If used, that counter name will be renamed in the Insights event to the value set here. If left out, the attribute in Insights will be named with the original name of that counter.
  * To retrieve properties from within a counter object, use the format `counter.property`, i.e. `targetInstance.DeviceID`.

#### Tips for finding/building new simple entries for `counterlist`

First, to get a list of all counter categories:

```powershell
PS C:\> Get-CimClass Win32_PerfFormattedData* | Select CimClassName
```

Let's take `root/cimv2:Win32_PerfFormattedData_MSSQLSQLEXPRESS_MSSQLSQLEXPRESSBufferManager` for example.

* provider = "MSSQLSQLEXPRESS"
* category = "MSSQLSQLEXPRESSBufferManager"

The format is `Win32_PerfFormattedData_{provider}_{category}`.

Get a list of all counters for that category:

```powershell
PS C:\> Get-CimInstance "Win32_PerfFormattedData_MSSQLSQLEXPRESS_MSSQLSQLEXPRESSBufferManager"

Caption               :
Description           :
Name                  :
Frequency_Object      :
Frequency_PerfTime    :
Frequency_Sys100NS    :
Timestamp_Object      :
Timestamp_PerfTime    :
Timestamp_Sys100NS    :
AWElookupmapsPersec   : 0
AWEstolenmapsPersec   : 0
AWEunmapcallsPersec   : 0
AWEunmappagesPersec   : 0
AWEwritemapsPersec    : 0
Buffercachehitratio   : 100
CheckpointpagesPersec : 0
Databasepages         : 247
FreeliststallsPersec  : 0
Freepages             : 396
LazywritesPersec      : 0
Pagelifeexpectancy    : 251325
PagelookupsPersec     : 56
PagereadsPersec       : 0
PagewritesPersec      : 0
ReadaheadpagesPersec  : 0
Reservedpages         : 0
Stolenpages           : 893
Targetpages           : 84612
Totalpages            : 1536
PSComputerName        :
```

* counter = "Buffercachehitratio"

Putting that all together, you would add the following under `counterlist`:

```javascript
{
	"provider": "MSSQLSQLEXPRESS",
	"category": "MSSQLSQLEXPRESSBufferManager",
	"counters": [{
		"counter": "Buffercachehitratio"
	}]
}
```

Optionally, you can include an `instance` property. You can see the following in the template.

```javascript
{
	"provider": "PerfOS",
	"category": "Processor",
	"instance": "_Total",
	"counters": [{
		"counter": "PercentProcessorTime"
	}]
}
```
There is an instance of the counter for each logical processor. The __total_ instance represents the sum of all of them.

If you run this, you'll see all of the instances and the `Name` property is the identifier.
```powershell
Get-CimInstance "Win32_PerfFormattedData_PerfOS_Processor"
```
