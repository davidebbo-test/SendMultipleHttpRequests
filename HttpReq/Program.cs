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
        static Dictionary<string, int> _servers = new Dictionary<string, int>();

        static void Main(string[] args)
        {
            string url = args[0];
            int maxIterations = Int32.Parse(args[1]);
            int countPerIteration = Int32.Parse(args[2]);
            int delay = Int32.Parse(args[3]);

            DoWorkAsync(url, maxIterations, countPerIteration, delay).Wait();

            foreach (var pair in _servers)
            {
                Console.WriteLine($"{pair.Key}: {pair.Value}");
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

            var response = action.Result;
            if (response.Headers.TryGetValues("X-server", out IEnumerable<string> values))
            {
                string serverName = values.First();
                int count;
                lock (_servers)
                {
                    _servers.TryGetValue(serverName, out count);
                    _servers[serverName] = count + 1;
                }
            }

            string requestContents = response.Content.ReadAsStringAsync().Result;

            if (action.Result.StatusCode == HttpStatusCode.OK) return;

            TimeSpan elapsed = DateTime.Now - invocationState.Start;
            Console.WriteLine($"{invocationState.Iteration}.{invocationState.Index}: {(int)elapsed.TotalMilliseconds}ms: {requestContents} {(int)action.Result.StatusCode} {action.Result.ReasonPhrase}");
        }

        class InvocationState
        {
            public DateTime Start { get; set; }
            public int Iteration { get; set; }
            public int Index { get; set; }
        }
    }
}