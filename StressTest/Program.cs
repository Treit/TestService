using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

Console.OutputEncoding = Encoding.UTF8;

var emojiMap = new Dictionary<HttpStatusCode, string>
{
    [HttpStatusCode.OK] = "😎",
    [HttpStatusCode.InternalServerError] = "😡",
    [HttpStatusCode.RequestTimeout] = "⏱️",
    [HttpStatusCode.NoContent] = "😭",
    [HttpStatusCode.NotFound] = "❓",
    [HttpStatusCode.MethodNotAllowed] = "🚫"
};

if (args.Length == 0)
{
    PrintUsage();
    return;
}

foreach (var arg in args)
{
    if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("/help", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("-help", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("--?")
        || arg.Equals("/?"))
    {
        PrintUsage();
        return;
    }
}

Console.WriteLine();
Console.WriteLine("Result Key");
Console.WriteLine("-----------------------------");
foreach (var (key, value) in emojiMap)
{
    Console.WriteLine($"{value} => {key}");
}
Console.WriteLine("-----------------------------");
Console.WriteLine();

ThreadPool.SetMinThreads(1000, 1000);
Console.OutputEncoding = Encoding.UTF8;

var sequential = false;
var dumpFailedIds = false;

var handler = new HttpClientHandler
{
    MaxConnectionsPerServer = 1000,
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    UseProxy = false,
};

var client = new HttpClient(handler)
{
    Timeout = TimeSpan.FromSeconds(1000)
};

var tasks = new List<Task>();
var callCount = args.Length > 0 ? int.Parse(args[0]) : 100;
var timings = new double[callCount];
var anyFailures = false;
var delayStart = true;
var successCount = 0;
var totalCount = 0;
var delayBetweenCalls = 0;
var writeToDisk = false;
var runId = Guid.NewGuid();
var writeRawBytes = false;
var opts = new JsonSerializerOptions { WriteIndented = true };
var failedActivityIds = new ConcurrentBag<(Guid, string)>();

foreach (var arg in args)
{
    if (arg.Equals("--no-delay", StringComparison.OrdinalIgnoreCase))
    {
        delayStart = false;
    }

    if (arg.Equals("--sequential", StringComparison.OrdinalIgnoreCase))
    {
        sequential = true;
    }

    if (arg.Equals("--write-response", StringComparison.OrdinalIgnoreCase))
    {
        writeToDisk = true;
    }

    if (arg.Equals("--write-raw", StringComparison.OrdinalIgnoreCase))
    {
        writeRawBytes = true;
    }

    if (arg.StartsWith("--delay-between-calls", StringComparison.OrdinalIgnoreCase))
    {
        sequential = true;
        delayBetweenCalls = int.Parse(arg.Split([':', '='])[1]);
    }
}

var url = "";
var body = "{}";

if (args.Length > 1)
{
    url = args[1];
}

if (args.Length > 2 && !args[2].StartsWith("--"))
{
    body = File.ReadAllText(args[2]);
}

var resetEvent = new ManualResetEvent(false);

for (int i = 0; i < callCount; i++)
{
    var num = i;
    var task = Task.Run(() =>
    {
        resetEvent.WaitOne();

        if (body == "{}")
        {
            MakeGetCall(num);
        }
        else
        {
            MakePostCall(num);
        }
    });

    if (sequential)
    {
        delayStart = false;
        resetEvent.Set();
        await task;
        await Task.Delay(delayBetweenCalls);
    }

    tasks.Add(task);
}

if (delayStart)
{
    Console.WriteLine("-   🔴");
    await Task.Delay(500);

    Console.WriteLine("3   🟡");
    await Task.Delay(500);

    Console.WriteLine("2   🟡");
    await Task.Delay(500);

    Console.WriteLine("1   🟡");
    await Task.Delay(500);

    while (!tasks.All(t => t.Status == TaskStatus.Running))
    {
        await Task.Delay(100);
    }
}

if (!sequential)
{
    Console.WriteLine("GO! 🟢");
}

resetEvent.Set();

await Task.WhenAll(tasks);

var avg = timings.Average();
var min = timings.Min();
var max = timings.Max();

Console.WriteLine();
var token = anyFailures ? "😨" : "😊";
var percentSuccess = successCount / (double)totalCount * 100;
var percentFailure = Math.Round(100.0 - percentSuccess, 2);

if (anyFailures)
{
    Console.WriteLine();
    Console.WriteLine($"🔥 Total: {totalCount} Success: {successCount} Failures: {totalCount - successCount}");
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Write($"🔥 {percentFailure}% failure.");
    Console.ResetColor();
    Console.WriteLine();
}

var timingLog = $"{token} [{DateTime.Now}] avg: {avg} ms. min: {min} ms. max: {max} ms. {percentSuccess}% success.";
Console.WriteLine();
Console.WriteLine(timingLog);
using var sw = new StreamWriter("timings.txt", true);
sw.WriteLine(timingLog);

if (dumpFailedIds && !failedActivityIds.IsEmpty)
{
    Console.WriteLine();
    Console.WriteLine("😔 Failed Activity Ids:");
    foreach (var (id, tag) in failedActivityIds)
    {
        Console.WriteLine($"{tag} {id}");
    }
}

void MakePostCall(int slot)
{
    try
    {
        var activityId = Guid.NewGuid();
        var localUrl = Regex.Replace(url, @"activityId=[a-fA-F0-9-]{36}", $"activityId={activityId}");
        var msg = new HttpRequestMessage(HttpMethod.Post, localUrl);
        msg.Headers.Add("X-Variants", "test");
        msg.Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
        msg.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var sw = Stopwatch.StartNew();
        var result = client.Send(msg);
        timings[slot] = sw.ElapsedMilliseconds;
        var token = result.StatusCode switch
        {
            HttpStatusCode.OK => emojiMap[HttpStatusCode.OK],
            HttpStatusCode.InternalServerError => emojiMap[HttpStatusCode.InternalServerError],
            HttpStatusCode.NoContent => emojiMap[HttpStatusCode.NoContent],
            HttpStatusCode.RequestTimeout => emojiMap[HttpStatusCode.RequestTimeout],
            HttpStatusCode.NotFound => emojiMap[HttpStatusCode.NotFound],
            HttpStatusCode.MethodNotAllowed => emojiMap[HttpStatusCode.MethodNotAllowed],
            _ => result.StatusCode.ToString()
        };

        Interlocked.Increment(ref totalCount);
        Console.Write(token);
        if (result.StatusCode != HttpStatusCode.OK)
        {
            anyFailures = true;
            failedActivityIds.Add((activityId, token));
        }
        else
        {
            Interlocked.Increment(ref successCount);
        }

        if (result.StatusCode == HttpStatusCode.InternalServerError)
        {
        }

        if (writeToDisk)
        {
            var fileName = $"response-{slot}-{runId}.json";

            if (writeRawBytes)
            {
                var response = result.Content.ReadAsByteArrayAsync().Result;
                File.WriteAllBytes(fileName, response);
            }
            else
            {
                var response = result.Content.ReadAsStringAsync().Result;

                var json = JsonSerializer.Serialize(
                    JsonSerializer.Deserialize<JsonElement>(response),
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(fileName, json);
            }
        }
    }
    catch (Exception e)
    {
        anyFailures = true;
        Console.WriteLine(e);
    }
}

void MakeGetCall(int slot)
{
    try
    {
        var activityId = Guid.NewGuid();
        var localUrl = Regex.Replace(url, @"activityId=[a-fA-F0-9-]{36}", $"activityId={activityId}");
        var msg = new HttpRequestMessage(HttpMethod.Get, localUrl);

        msg.Headers.Add("X-Variants", "test");
        var sw = Stopwatch.StartNew();
        var result = client.Send(msg);
        timings[slot] = sw.ElapsedMilliseconds;
        var token = result.StatusCode switch
        {
            HttpStatusCode.OK => emojiMap[HttpStatusCode.OK],
            HttpStatusCode.InternalServerError => emojiMap[HttpStatusCode.InternalServerError],
            HttpStatusCode.NoContent => emojiMap[HttpStatusCode.NoContent],
            HttpStatusCode.RequestTimeout => emojiMap[HttpStatusCode.RequestTimeout],
            HttpStatusCode.NotFound => emojiMap[HttpStatusCode.NotFound],
            HttpStatusCode.MethodNotAllowed => emojiMap[HttpStatusCode.MethodNotAllowed],
            _ => result.StatusCode.ToString()
        };

        Interlocked.Increment(ref totalCount);

        Console.Write(token);
        if (result.StatusCode != HttpStatusCode.OK)
        {
            anyFailures = true;
            failedActivityIds.Add((activityId, token));
        }
        else
        {
            Interlocked.Increment(ref successCount);
        }

        if (result.StatusCode == HttpStatusCode.InternalServerError)
        {
            //Console.WriteLine(result.Content.ReadAsStringAsync().Result);
        }

        if (writeToDisk)
        {
            var fileName = $"response-{slot}-{runId}.json";

            if (writeRawBytes)
            {
                var response = result.Content.ReadAsByteArrayAsync().Result;
                File.WriteAllBytes(fileName, response);
            }
            else
            {
                var response = result.Content.ReadAsStringAsync().Result;

                var json = JsonSerializer.Serialize(
                    JsonSerializer.Deserialize<JsonElement>(response),
                    opts);

                File.WriteAllText(fileName, json);
            }
        }
    }
    catch (Exception e)
    {
        anyFailures = true;
        Console.WriteLine(e);
    }
}

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run [count] [url] <[post-payload-file]> [--no-delay] [--sequential] [--write-response] [--write-raw] [--delay-between-calls:<n>] [--help]");
    Console.WriteLine("count: Number of calls to make. Default is 100. Will run in parallel by default.");
    Console.WriteLine("url: The URL to call.");
    Console.WriteLine("--no-delay: Start all tasks immediately.");
    Console.WriteLine("--sequential: Run the calls sequentially instead of in parallel. Implies --no-delay.");
    Console.WriteLine("--write-response: Write the response to disk.");
    Console.WriteLine("--write-raw: Write the raw bytes of the response to disk.");
    Console.WriteLine("--delay-between-calls: How many milliseconds to wait between calls. Implies sequential.");
    Console.WriteLine("--help: Show this help.");
}