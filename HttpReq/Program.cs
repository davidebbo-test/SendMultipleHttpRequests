using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HttpReq
{
    class Program
    {
        static object _lock = new object();
        static Dictionary<string, int> _servers = new Dictionary<string, int>();
        static Dictionary<int, int> _statusCodes = new Dictionary<int, int>();
        static TimeSpan _totalTime = new TimeSpan();
        static int _totalRequests = 0;

        static void Main(string[] args)
        {
            string url = args[0];
            int maxIterations = Int32.Parse(args[1]);
            int countPerIteration = Int32.Parse(args[2]);
            int delay = Int32.Parse(args[3]);

            DoWorkAsync(url, maxIterations, countPerIteration, delay).Wait();

            Console.WriteLine();
            Console.WriteLine("Request count by status code");
            foreach (var pair in _statusCodes)
            {
                Console.WriteLine($"  {pair.Key}: {pair.Value}");
            }

            Console.WriteLine($"Test input:");
            Console.WriteLine($"  URL: {url}");
            Console.WriteLine($"  Iterations: {maxIterations}");
            Console.WriteLine($"  Requests per iteration: {countPerIteration}");
            Console.WriteLine($"  Delay between iterations: {delay}");

            Console.WriteLine();
            Console.WriteLine($"Average request time: {(int)(_totalTime / _totalRequests).TotalMilliseconds}ms");

            Console.WriteLine();
            Console.WriteLine("Request count by server");
            foreach (var pair in _servers)
            {
                Console.WriteLine($"  {pair.Key}: {pair.Value}");
            }
        }

        static async Task DoWorkAsync(string url, int maxIterations, int countPerIteration, int delay)
        {
            var tasks = new List<Task>();

            // Generate a bunch of clients to avoid getting requests affinitized to a FrontEnd
            var clients = new HttpClient[50];
            for (int i = 0; i < clients.Length; i++)
            {
                clients[i] = new HttpClient();
                clients[i].Timeout = Timeout.InfiniteTimeSpan;
            }

            for (int currentIteration = 0; currentIteration < maxIterations; currentIteration++)
            {
                Console.WriteLine($"Iteration {currentIteration + 1}: generating {countPerIteration} requests");

                for (int index = 0; index < countPerIteration; index++)
                {
                    HttpClient client = clients[(new Random()).Next() % clients.Length];

                    //Console.WriteLine($"Starting request {index} of iteration {currentIteration}");
                    tasks.Add(client.GetAsync(url).ContinueWith(RequestDone, new InvocationState { Start = DateTime.Now, Iteration = currentIteration, Index = index }));
                }

                await Task.Delay(delay);
            }

            await Task.WhenAll(tasks);
        }

        static void RequestDone(Task<HttpResponseMessage> action, object state)
        {
            var invocationState = (InvocationState)state;
            TimeSpan elapsed = DateTime.Now - invocationState.Start;

            HttpResponseMessage response = null;
            int statusCode;

            try
            {
                response = action.Result;
                statusCode = (int)response.StatusCode;
            }
            catch (Exception)
            {
                // Probably a timeout
                statusCode = 0;
            }

            lock (_lock)
            {
                if (response != null)
                {
                    if (response.Headers.TryGetValues("X-server", out IEnumerable<string> values))
                    {
                        string serverName = values.First();
                        _servers.TryGetValue(serverName, out int count);
                        _servers[serverName] = count + 1;
                    }
                }

                _totalTime += elapsed;
                _totalRequests++;

                _statusCodes.TryGetValue(statusCode, out int statusCount);
                _statusCodes[statusCode] = statusCount + 1;
            }

            if (statusCode == 0)
            {
                Console.WriteLine("The request timed out");
                return;
            }

            string requestContents = response.Content.ReadAsStringAsync().Result;

            if (action.Result.StatusCode == HttpStatusCode.OK) return;

            Console.WriteLine($"{invocationState.Iteration}.{invocationState.Index}: {(int)elapsed.TotalMilliseconds}ms: {requestContents} {statusCode} {action.Result.ReasonPhrase}");
        }

        class InvocationState
        {
            public DateTime Start { get; set; }
            public int Iteration { get; set; }
            public int Index { get; set; }
        }
    }
}