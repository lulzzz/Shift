# Shift
Shift background or long running jobs into reliable and durable workers out of your main application. Shift enables your application to easily run long running jobs in separate infrastructures. 

**Features:**
- Open source and free - including commercial use.
- Reliable and durable background and long running jobs.
- Out of band processing of long running jobs. 
- Ability to pause, stop, reset, and restart long running jobs.
- Auto removal of older jobs.
- Scale out with multiple Shift servers to run large number of jobs.
- Multiple choices of persistent storage: Redis, MongoDB, Microsoft SQL server, or Azure DocumentDB. 
- Optional progress tracking for each running jobs.
- Optional encryption for serialized data.
- Run Shift Server in your own .NET apps, Azure WebJobs, or Windows services. 

The client library allows client apps to add jobs and send commands to Shift server to pause, stop, delete, reset, and run jobs.

A simple example of adding a job:
```
var job = new TestJob();
var jobID = jobClient.Add(() => job.Start("Hello World"));
```

Add a long running job with cancellation token that periodically reports its progress:
```
var job = new TestJob();
var progress = new SynchronousProgress<ProgressInfo>();
var cancelToken = (new CancellationTokenSource()).Token; 
var pauseToken = (new PauseTokenSource()).Token;
var jobID = jobClient.Add("Shift.Demo.Client", () => job.Start("Hello World", progress, cancelToken, pauseToken));
```

The server component checks for available jobs through polling, using first-in, first-out (FIFO) queue method. The server is a simple .NET library and needs to run inside a .NET app, Azure WebJob, or Windows service. 

Sample Shift server projects are provided as a starting point:
- [Shift.WinService](https://github.com/hhalim/Shift.WinService) is the standalone Windows service server component, multiple services can be installed in the same server. 
- [Shift.Topshelf](https://github.com/hhalim/Shift.Topshelf) is the open source Topshelf package version for Windows service. This project allows simpler debugging and deployment to Windows.
- [Shift.WebJob](https://github.com/hhalim/Shift.WebJob) is the Azure cloud WebJob app, multiple web jobs can also be deployed to multiple Azure App Services. 

## Demos
Please check out the demo apps first to provide better understanding on how to integrate Shift into your own .NET application. There is the ASP.NET MVC demo that shows Shift client and server running in the same ASP.Net process, and the simpler console Shift client and server apps demo. The console apps are two separate projects that demonstrate the client and the server working in two different processes.
- ASP.NET MVC demo: [Shift.Demo.Mvc](https://github.com/hhalim/Shift.Demo.Mvc)
- ASP.NET Core MVC demo: [Shift.Demo.Mvc.Core](https://github.com/hhalim/Shift.Demo.Mvc.Core)
- Console apps demo: [Shift.Demo.Client](https://github.com/hhalim/Shift.Demo.Client) and [Shift.Demo.Server](https://github.com/hhalim/Shift.Demo.Server)

## Quick Start and More
Shift package is on [nuget.org](https://www.nuget.org/packages/Shift), but first check out the Shift wiki for the [Quick Start](https://github.com/hhalim/Shift/wiki/Quick-Start). Other important topics in the wiki: [Assembly Loading](https://github.com/hhalim/Shift/wiki/Assembly-Loading), [Scheduling, Batch and Continuation jobs](https://github.com/hhalim/Shift/wiki/Schedule-Batch-Continuation), [Message Queuing](https://github.com/hhalim/Shift/wiki/Message-Queuing), and more. 

## Credits
Shift uses the following open source projects:
- [Autofac](http://autofac.org/)
- [Dapper](https://github.com/StackExchange/Dapper)
- [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis)
- [Json.NET](http://james.newtonking.com/json)
- [MongoDB C# Driver](https://github.com/mongodb/mongo-csharp-driver)
- [Azure DocumentDB .NET SDK](https://github.com/Azure/azure-documentdb-dotnet)
- [xUnit](https://github.com/xunit/xunit)
- [Moq](https://github.com/moq/moq)


