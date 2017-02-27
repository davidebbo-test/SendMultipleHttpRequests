using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace HttpReq
{
    class Program
    {
        static void Main(string[] args)
        {
            DoWorkAsync(args[0], Int32.Parse(args[1]), Int32.Parse(args[2])).Wait();
        }

        static async Task DoWorkAsync(string url, int iterations, int delay)
        {
            var tasks = new List<Task>();

            using (var client = new HttpClient())
            {

                for (int i = 0; i < iterations; i++)
                {
                    Console.WriteLine($"Starting request {i}");
                    tasks.Add(client.GetStringAsync(url).ContinueWith(RequestDone, new InvocationState { Start = DateTime.Now, Iteration = i }));
                    await Task.Delay(delay);
                }

                await Task.WhenAll(tasks);
            }
        }

        static void RequestDone(Task<string> action, object state)
        {
            var invocationState = (InvocationState)state;
            Console.WriteLine($"{invocationState.Iteration}: {DateTime.Now - invocationState.Start}: {action.Result}");
        }

        class InvocationState
        {
            public DateTime Start { get; set; }
            public int Iteration { get; set; }
        }
    }
}