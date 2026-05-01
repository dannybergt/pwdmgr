using Pwdmgr.Agent.Service;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Privora Agent";
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

