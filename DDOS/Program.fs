open System
open System.Net
open System.Net.Http


ServicePointManager.DefaultConnectionLimit <- 2000
ServicePointManager.Expect100Continue <- false
ServicePointManager.UseNagleAlgorithm <- false
ServicePointManager.SecurityProtocol <- SecurityProtocolType.Tls12 ||| SecurityProtocolType.Tls11 ||| SecurityProtocolType.Tls
ServicePointManager.EnableDnsRoundRobin <- true
ServicePointManager.DnsRefreshTimeout <- 60*60*1000

Threading.ThreadPool.SetMinThreads (512, 512) |> ignore

let testEndpoint = "http://localhost:9000"

let httpClient = new HttpClient(BaseAddress = Uri(testEndpoint))

let send (endpoint : string) req = async {
  let request = new HttpRequestMessage(HttpMethod.Post, endpoint, Content = new StringContent(req)) 
  let! response = httpClient.SendAsync request |> Async.AwaitTask 
  return! response.Content.ReadAsStringAsync() |> Async.AwaitTask 
}

let BATCH = 24
let rand = System.Random()

[<EntryPoint>]
let main _ =    

  //let ids = 
  //  System.IO.File.ReadAllLines( __SOURCE_DIRECTORY__ + "\\sample_gen.csv" )
  //  |> Seq.skip 1
  //  |> Seq.collect(fun s -> s.Replace("\"", "").Replace("[", "").Split(','))
  //  |> Array.ofSeq
  printfn "Loading test data..."
  let ids = (send "test_data" "1000" |> Async.RunSynchronously).Split(',')
  
  let randomIds () = 
    let rec loop l = 
        if Set.count l < BATCH then
            Set.add (rand.Next(ids.Length - 1)) l |> loop 
        else
            l
    loop Set.empty 

  let getLine () = randomIds () |> Seq.map (fun x -> ids.[x]) |> String.concat ","
  
  let tests = [2, 10000] 
  
  printfn "Starting tests..."
  async {
    for (sleepTime, count) in tests do
      printfn "%A Sleep: %dms Request count: %d" DateTime.Now.TimeOfDay sleepTime count
      for _ in 1..count do
        if sleepTime > 0 then do! Async.Sleep sleepTime
        getLine()
        |> send "req" 
        |> Async.Ignore
        |> Async.Start
  } |> Async.RunSynchronously
  printfn "Press any key..."
  Console.ReadKey() |> ignore
  0
