module Client

open System
open Microsoft.Azure.Documents
open Microsoft.Azure.Documents.Client
open Microsoft.Azure.Documents.Linq

let Account = "<account>"
let Key = "<key>"
let dbName = "<db>"
let collectionName = "<collection>"
let mutable LOG_QUERIES = false

let logQueries (feed : FeedResponse<_>) = 
      if LOG_QUERIES then          
        for KeyValue(k, v) in feed.QueryMetrics do
          printfn "partition=%s|doc_count=%d|doc_size=%d|time=%f" k v.RetrievedDocumentCount v.RetrievedDocumentSize v.TotalTime.TotalMilliseconds      
      feed

let connectionPolicy = 
  ConnectionPolicy(
              ConnectionMode = ConnectionMode.Direct,
              ConnectionProtocol = Protocol.Tcp,
              RetryOptions = RetryOptions(
                                  MaxRetryAttemptsOnThrottledRequests = 9,
                                  MaxRetryWaitTimeInSeconds = 30),
              MaxConnectionLimit = 1000,
              RequestTimeout = TimeSpan.FromSeconds(4.))


let client = new DocumentClient( Uri(Account), Key, connectionPolicy)

let collectionUri = UriFactory.CreateDocumentCollectionUri(dbName, collectionName)

let printException e =
  let rec loop (e : exn) =
    seq {
      yield e
      if e.InnerException <> null
      then
          yield! loop e.InnerException
      match e with
      | :? AggregateException as e ->
        if e.InnerExceptions <> null && e.InnerExceptions.Count > 0 then
            yield! e.InnerExceptions |> Seq.collect loop
      | _ -> ()
      }
  e
  |>  loop
  |> Seq.map(fun e -> e.Message)
  |> Seq.distinct
  |> Seq.filter (fun m-> m <> "One or more errors occurred.")
  |> String.concat "\n"
  |> printfn "%s"

let runQuery feedOptions (query: string) = async {   
    let query = client.CreateDocumentQuery(collectionUri, query, feedOptions ).AsDocumentQuery()
    let start = DateTime.Now
    let rec loop () = async {
      if query.HasMoreResults then 
        let! res = query.ExecuteNextAsync<Document>() |> Async.AwaitTask |> Async.Catch
        match res with
        | Choice1Of2 feed ->
          let! tail = loop ()
          return feed::tail
        | Choice2Of2 ex ->
          printException ex
          return []
      else
        return []
    }
    let! results = loop ()
    let results = results |> Seq.collect logQueries |> Array.ofSeq
    return results, (int)(DateTime.Now - start).TotalMilliseconds
}

let testData count = async {
    //This call is needed because TOP cross-partition query returns results from only one partition range
  let! pkrs = client.ReadPartitionKeyRangeFeedAsync collectionUri |> Async.AwaitTask
  let countPerPartition = count / pkrs.Count + 1
  let! ids = 
    pkrs
    |> Seq.map(fun pkr ->
      let feedOptions =
              FeedOptions (
                MaxItemCount =  Nullable<_> count,
                PopulateQueryMetrics = LOG_QUERIES,
                MaxDegreeOfParallelism = 100, 
                MaxBufferedItemCount = count,
                PartitionKeyRangeId = pkr.Id
                )
      /// STARTSWITH is to try to get better distribution of keys from each partition range, experiments showed
      /// that data fetched with just TOP query produces much lower latency than real-life examples with well distributed keys
      countPerPartition |> sprintf "SELECT top %d c.id FROM c WHERE STARTSWITH(c.id, '00')" |> runQuery feedOptions 
      )
    |> Async.Parallel
  
  return ids |> Seq.collect fst |> Seq.take count |> Seq.map(fun d -> d.Id) |> Array.ofSeq
}
 
let mget docIds =
    let feedOptions =
      FeedOptions (
        EnableCrossPartitionQuery = true,
        MaxItemCount =  Nullable<_> 1000,
        PopulateQueryMetrics = LOG_QUERIES,
        MaxDegreeOfParallelism = 100, 
        MaxBufferedItemCount = Array.length docIds
        )
    let query = 
      let ids =
        docIds
        |> Seq.map (fun k -> String.Concat [| "'"; k; "'" |])
        |> String.concat ","
      String.Concat [| "select * from c where c.id in ("; ids; ")" |]
    runQuery feedOptions query
