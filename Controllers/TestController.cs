using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Test
{
    public record TestResponse(string Message);
    public record TestRequest(string Input);

    class Test { }

    [ApiController]
    [Route("[controller]")]
    public class TestController : ControllerBase
    {
        private readonly ILogger<TestController> _logger;
        private readonly IServiceCollection _serviceCollection;

        public TestController(
            ILogger<TestController> logger,
            IServiceCollection collection,
            BackGroundWorker worker)
        {
            _logger = logger;
            _serviceCollection = collection;
            _serviceCollection.AddSingleton<Test>(new Test());
        }

        [HttpPost]
        public TestResponse Post(TestRequest request)
        {
            return new TestResponse(request.Input.ToUpperInvariant());
        }
    }
}
