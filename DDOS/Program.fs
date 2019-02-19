open System
open System.Net.Http

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
  let ids = (send "test_data" "5000" |> Async.RunSynchronously).Split(',')
  
  //Inject some random ids to mimic 404s
  let ids = Array.append ids [| for _ in 1..100 -> System.Guid.NewGuid().ToString("N") |]

  let getLine () = Seq.init BATCH (fun _ -> ids.[rand.Next(ids.Length - 1)])|> String.concat ","
  
  let tests = [5, 10000] 
  
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
