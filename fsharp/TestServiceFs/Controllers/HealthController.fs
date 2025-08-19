namespace TestServiceFs.Controllers

open System
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging

type HealthCheckResponse = { Status: string }

[<ApiController>]
[<Route("[controller]")>]
type HealthController (logger : ILogger<HealthController>) =
    inherit ControllerBase()

    [<HttpGet>]
    member _.Get() =
        BadRequestObjectResult("BAD")
