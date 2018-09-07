﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace HttpReq
{
    class Program
    {
        static object _lock = new object();
        static Dictionary<string, int> _servers = new Dictionary<string, int>();
        static Dictionary<int, int> _statusCodes = new Dictionary<int, int>();
        static List<double> _latencies = new List<double>();
        static TimeSpan _totalTime = new TimeSpan();
        static int _totalRequests = 0;
        static HttpClient[] _clients;


        static void Main(string[] args)
        {
            if (args.Length < 4 || args.Length > 5)
            {
                Console.WriteLine("Usage: dotnet HttpReq.dll {client | timer} {url} {iterations} {reqPerIteration} [{SleepBetweenIterationsInMS}]");
                return;
            }

            DateTimeOffset start = DateTimeOffset.UtcNow;
            int i = 0;
            bool clientMode = String.Equals(args[i++], "client", StringComparison.OrdinalIgnoreCase);
            string url = args[i++];
            int maxIterations = Int32.Parse(args[i++]);
            int countPerIteration = Int32.Parse(args[i++]);
            int delay = args.Length == 5 ? Int32.Parse(args[i++]) : 0;

            SendRequestsAsync(url, maxIterations, countPerIteration, delay, clientMode).Wait();

            var totalTestTime = DateTimeOffset.UtcNow - start;

            Console.WriteLine();
            Console.WriteLine($"Test input:");
            Console.WriteLine($"  UTC time: {start.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'")}");
            Console.WriteLine($"  URL: {url}");
            Console.WriteLine($"  Iterations: {maxIterations}");
            Console.WriteLine($"  Requests per iteration: {countPerIteration}");
            Console.WriteLine($"  Delay between iterations: {delay}");

            Console.WriteLine();
            Console.WriteLine($"Total test time: {(int)totalTestTime.TotalMilliseconds}ms");
            Console.WriteLine($"Total requests: {countPerIteration * maxIterations}");
            Console.WriteLine($"Throughput: {((countPerIteration * maxIterations) / totalTestTime.TotalSeconds).ToString("#.##")} rps");
            Console.WriteLine($"Min latency: {(int)_latencies.Min()}ms");
            Console.WriteLine($"Avg. latency: {(int)_latencies.Average()}ms");
            Console.WriteLine($"Max latency: {(int)_latencies.Max()}ms");
            Console.WriteLine($"95th latency percentile: {(int)Percentile(_latencies.ToArray(), 0.95)}ms");
            Console.WriteLine($"99th latency percentile: {(int)Percentile(_latencies.ToArray(), 0.99)}ms");

            Console.WriteLine();
            Console.WriteLine("Request count by status code");
            foreach (var pair in _statusCodes)
            {
                Console.WriteLine($"  {pair.Key}: {pair.Value}");
            }

            Console.WriteLine();
            Console.WriteLine($"Request count by server ({_servers.Count} servers)");
            foreach (var pair in _servers.OrderBy(s => s.Key))
            {
                Console.WriteLine($"  {pair.Key}: {pair.Value}");
            }
        }

        static async Task SendRequestsAsync(string url, int maxIterations, int countPerIteration, int delay, bool clientMode)
        {
            // Generate a bunch of clients to avoid getting requests affinitized to a FrontEnd
            _clients = new HttpClient[50];
            for (int i = 0; i < _clients.Length; i++)
            {
                _clients[i] = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(120)
                };

                _clients[i].DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }

            var tasks = new List<Task>();

            if (clientMode)
            {
                // Client mode: a bunch of clients each send requests one after another
                for (int index = 0; index < countPerIteration; index++)
                {
                    tasks.Add(SendRequestsAsyncOneClient(url, maxIterations, delay, index));
                }
            }
            else
            {
                // Timer mode, we send 'countPerIteration' requests every 'delay', regardless of how long they take
                for (int currentIteration = 0; currentIteration < maxIterations; currentIteration++)
                {
                    for (int index = 0; index < countPerIteration; index++)
                    {
                        tasks.Add(
                            GetHttpClient().GetAsync(url).ContinueWith(
                                RequestDone, new InvocationState { Start = DateTimeOffset.UtcNow, Iteration = currentIteration, Index = index }));
                    }

                    await Task.Delay(delay);
                }
            }

            await Task.WhenAll(tasks);
        }

        static async Task SendRequestsAsyncOneClient(string url, int maxIterations, int delay, int clientIndex)
        {
            for (int currentIteration = 0; currentIteration < maxIterations; currentIteration++)
            {
                var invocationState = new InvocationState { Start = DateTimeOffset.UtcNow, Iteration = currentIteration, Index = clientIndex };
                await RequestDone(
                    GetHttpClient().GetAsync(url),
                    invocationState
                );

                await Task.Delay(delay);
            }
        }

        static HttpClient GetHttpClient()
        {
            return _clients[(new Random()).Next() % _clients.Length];
        }

        static async Task RequestDone(Task<HttpResponseMessage> action, object state)
        {
            var invocationState = (InvocationState)state;

            HttpResponseMessage response = null;
            int statusCode;

            try
            {
                response = await action;
                statusCode = (int)response.StatusCode;
            }
            catch (Exception)
            {
                // Probably a timeout
                statusCode = 0;
            }

            TimeSpan elapsed = DateTimeOffset.UtcNow - invocationState.Start;

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
                _latencies.Add(elapsed.TotalMilliseconds);
            }

            if (statusCode == 0)
            {
                Console.WriteLine("The request timed out");
                return;
            }

            if (action.Result.StatusCode == HttpStatusCode.OK)
            {
                return;
            }

            string requestContents = response.Content.ReadAsStringAsync().Result;
            Console.WriteLine($"{invocationState.Iteration}.{invocationState.Index}: {(int)elapsed.TotalMilliseconds}ms: {requestContents} {statusCode} {action.Result.ReasonPhrase}");
        }

        static double Percentile(double[] sequence, double excelPercentile)
        {
            Array.Sort(sequence);
            int N = sequence.Length;
            double n = (N - 1) * excelPercentile + 1;

            if (n == 1d)
            {
                return sequence[0];
            }
            else if (n == N)
            {
                return sequence[N - 1];
            }
            else
            {
                int k = (int)n;
                double d = n - k;
                return sequence[k - 1] + d * (sequence[k] - sequence[k - 1]);
            }
        }

        class InvocationState
        {
            public DateTimeOffset Start { get; set; }
            public int Iteration { get; set; }
            public int Index { get; set; }
        }
    }
}