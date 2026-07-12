var builder = DistributedApplication.CreateBuilder(args);


var username = builder.AddParameter("username", secret: true);
var password = builder.AddParameter("password", secret: true);
var rabbitmq = builder.AddRabbitMQ("messaging", username, password)
    .WithImageTag("4.1-management")
    .WithManagementPlugin();


var redis_cache = builder.AddRedis("rediscache");


var apiService = builder.AddProject<Projects.AspireTemplate_ApiService>("apiservice")
    .WithReference(rabbitmq)
    .WithReference(redis_cache);


builder.AddProject<Projects.AspireTemplate_Web>("webfrontend")
    .WithReference(apiService);

builder.AddProject<Projects.RabbitMQConsumers>("consumers")
    .WithReference(rabbitmq);

builder.Build().Run();
