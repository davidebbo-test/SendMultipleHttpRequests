using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HttpReq
{
    class Program
    {
        static void Main(string[] args)
        {
            string url = args[0];
            int maxIterations = Int32.Parse(args[1]);
            int countPerIteration = Int32.Parse(args[2]);
            int delay = Int32.Parse(args[3]);

            DoWorkAsync(url, maxIterations, countPerIteration, delay).Wait();
        }

        static async Task DoWorkAsync(string url, int maxIterations, int countPerIteration, int delay)
        {
            var tasks = new List<Task>();

            using (var client = new HttpClient())
            {
                client.Timeout = Timeout.InfiniteTimeSpan;

                for (int currentIteration = 0; currentIteration < maxIterations; currentIteration++)
                {
                    Console.WriteLine($"Iteration {currentIteration + 1}: generating {countPerIteration} requests");

                    for (int index = 0; index < countPerIteration; index++)
                    {
                        //Console.WriteLine($"Starting request {index} of iteration {currentIteration}");
                        tasks.Add(client.GetAsync(url).ContinueWith(RequestDone, new InvocationState { Start = DateTime.Now, Iteration = currentIteration, Index = index }));
                    }

                    await Task.Delay(delay);
                }

                await Task.WhenAll(tasks);
            }
        }

        static void RequestDone(Task<HttpResponseMessage> action, object state)
        {
            var invocationState = (InvocationState)state;

            string requestContents = action.Result.Content.ReadAsStringAsync().Result;

            if (action.Result.StatusCode == HttpStatusCode.OK) return;

            TimeSpan elapsed = DateTime.Now - invocationState.Start;
            Console.WriteLine($"{invocationState.Iteration}.{invocationState.Index}: {(int)elapsed.TotalMilliseconds}ms: {requestContents} {action.Result.StatusCode}");
        }

        class InvocationState
        {
            public DateTime Start { get; set; }
            public int Iteration { get; set; }
            public int Index { get; set; }
        }
    }
}