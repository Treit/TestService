using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Threading.Tasks;

namespace Test
{
    public record TestResponse(string Message);
    public record TestRequest(string Input);

    class Test { }

    [ApiController]
    [Route("[controller]")]
    public class TestController : ControllerBase
    {
        private static readonly HttpClient s_httpClient = new HttpClient();
        private readonly ILogger<TestController> _logger;
        private readonly IServiceCollection _serviceCollection;

        public TestController(
            ILogger<TestController> logger,
            IServiceCollection collection)
        {
            _logger = logger;
            _serviceCollection = collection;
            _serviceCollection.AddSingleton<Test>(new Test());
        }

        [HttpPost("HttpGetTest")]
        public async Task<TestResponse> HttpGetTest(TestRequest request)
        {
            var response = await s_httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, request.Input));
            return new TestResponse(await response.Content.ReadAsStringAsync());
        }

        [HttpPost("Test")]
        public TestResponse Test(TestRequest request)
        {
            _logger.LogInformation("{requestInput}", request.Input);
            return new TestResponse(request.Input.ToUpperInvariant());
        }

        [HttpPost("DoSomething")]
        public void DoSomething()
        {
            _logger.LogInformation("DoSomething called.");
        }
    }
}
