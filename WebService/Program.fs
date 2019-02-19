module Main

open System
open System.Net.Http
open System.Net
open System.Collections.Concurrent
open System.Threading
open Microsoft.Azure.Documents

open HttpHost

ServicePointManager.DefaultConnectionLimit <- 2000
ServicePointManager.Expect100Continue <- false
ServicePointManager.UseNagleAlgorithm <- false
ServicePointManager.SecurityProtocol <- SecurityProtocolType.Tls12 ||| SecurityProtocolType.Tls11 ||| SecurityProtocolType.Tls
ServicePointManager.EnableDnsRoundRobin <- true
ServicePointManager.DnsRefreshTimeout <- 60*60*1000

Threading.ThreadPool.SetMinThreads (512, 512) |> ignore

let queue = ref 0
let stats = new BlockingCollection<_>(1000)

let getTestData (req : HttpRequestMessage ) = async {
  let! body = req.Content.ReadAsStringAsync() |> Async.AwaitTask
  match Int32.TryParse body with
  | true, count -> 
    let! data = Client.testData count
    let content = new StringContent(data |> String.concat ",")    
    return new HttpResponseMessage(HttpStatusCode.OK, Content = content)
  | false, _ -> 
    return new HttpResponseMessage(HttpStatusCode.BadRequest)
}

let service (req : HttpRequestMessage ) = async {
  let start = DateTime.UtcNow
  let globalStart = start
  let! body = req.Content.ReadAsStringAsync() |> Async.AwaitTask
  let reading = (int)(DateTime.UtcNow - start).TotalMilliseconds
  Interlocked.Increment queue |> ignore
  let requestedIds = body.Split ',' 
  match requestedIds with
  | [||] -> return new HttpResponseMessage(HttpStatusCode.BadRequest)
  | skus -> 
      let! (docs, metrics) = Client.mget skus
      let notFound = requestedIds.Length - docs.Length
      let gl = (int)(DateTime.UtcNow - globalStart).TotalMilliseconds
      System.Threading.Interlocked.Decrement queue |> ignore
      if not (stats.TryAdd [| reading; metrics; gl; notFound |]) then
        printfn "Discarding results, stats queue is full"
      return new HttpResponseMessage(HttpStatusCode.OK)
} 

let maxCpu, maxIo = System.Threading.ThreadPool.GetMaxThreads ()
  
let report _ =
  try
    let data = 
      let rec loop () =
        match stats.TryTake () with | true, x -> x::loop () | _ -> []
      loop ()
      
    let cpu, io = System.Threading.ThreadPool.GetAvailableThreads ()
    //let parseRequest = data |> Array.map (fun x -> x.[0])
    //let mgetCall = data |> Array.map (fun x -> x.[1])
    let notFound = data |> List.map (fun x -> x.[3]) |> List.sum 
    let total = data |> List.map (fun x -> x.[2]) |> Array.ofList |> Array.sort
    let p99, avg = 
      if total.Length = 0 then
        0,0
      else
        total.[total.Length - 1 - (total.Length / 99)], (Seq.sum total) / total.Length  
    printfn "%A Queue: %d Completed: %d NotFound: %d Latency: [avg: %d p99: %d] CPU: %d IO: %d" 
      DateTime.Now.TimeOfDay 
      !queue
      total.Length
      notFound
      avg
      p99
      (maxCpu - cpu) 
      (maxIo - io)
   with
   | ex -> printfn "Error: %s" ex.Message

[<EntryPoint>]
let main args =
  Client.LOG_QUERIES <- (args |> Seq.contains "debug")
  let timer = new Timer(TimerCallback(report), null, 10000, 10000)  
  [
    host "http://*:9000/req/" service 
    host "http://*:9000/test_data/" getTestData 
  ]
  |> Async.Parallel
  |> Async.Ignore
  |> Async.RunSynchronously
  timer.Dispose()
  0 

