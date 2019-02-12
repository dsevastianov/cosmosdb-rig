module HttpHost

open System
open System.Net
open System.Net.Http
open System.Threading


type HttpListener with member this.AsyncGetContext() = Async.FromBeginEnd(this.BeginGetContext, this.EndGetContext)

let isNotNull a = not <| obj.ReferenceEquals(null, a)

let host uriPrefix (service: HttpRequestMessage -> Async<HttpResponseMessage>) =

  let listener = new HttpListener()
  listener.Prefixes.Add(uriPrefix)
  listener.AuthenticationSchemes <- AuthenticationSchemes.Anonymous
  listener.UnsafeConnectionNtlmAuthentication <- true
  listener.Start()

  let asHttpRequest (ctx:HttpListenerContext) =
    let request = 
      new HttpRequestMessage(
        new HttpMethod(ctx.Request.HttpMethod), 
        ctx.Request.Url,
        Version = ctx.Request.ProtocolVersion,
        Content = new StreamContent(ctx.Request.InputStream))
    for i = 0 to (ctx.Request.Headers.Count - 1) do
      let name = ctx.Request.Headers.GetKey(i)
      let values = ctx.Request.Headers.GetValues(i)
      if (isNotNull name && not (name.StartsWith("Content"))) then request.Headers.Add(name, values)
      else request.Content.Headers.Add(name, values)
    request

  let fill (res:HttpResponseMessage) (ctx:HttpListenerContext) = async {
    ctx.Response.StatusCode <- int res.StatusCode
    for pair in res.Headers do
      if isNotNull pair.Value then
        for value in pair.Value do
          ctx.Response.Headers.Add(pair.Key, value)
    if isNotNull res.Content then
      if isNotNull res.Content.Headers then
        for pair in res.Content.Headers do
          for value in pair.Value do
            ctx.Response.Headers.Add(pair.Key, value)
        do! res.Content.CopyToAsync(ctx.Response.OutputStream) |> Async.AwaitTask }

  let tryDumpReqBody (req:HttpRequestMessage) = async {
    try
      if req.Method = HttpMethod.Post then
        let! body = req.Content.ReadAsStringAsync() |> Async.AwaitTask
        return body
      else
        return null
    with _ ->
      return null }

  let handle (ctx:HttpListenerContext) = async {
    try
      try
        use req = asHttpRequest ctx
        try
          use! res = service req
          do! fill res ctx
        with ex ->
          let! body = tryDumpReqBody req
          printfn "Unhandled Exception|error=%A|http_method=%s|http_url=%s|body=%s\nfull_exception=%A" ex req.Method.Method req.RequestUri.AbsoluteUri body ex
          let res = new HttpResponseMessage(HttpStatusCode.InternalServerError)
          res.RequestMessage <- req
          res.Content <- new StringContent(ex.ToString())
          do! fill res ctx
      with ex ->
        printfn "Failed to convert to Http Request|error=%A|http_url=%s" ex ctx.Request.Url.AbsoluteUri
    finally
      ctx.Response.Close() }

  let ctx = listener.AsyncGetContext()
  let cts = new CancellationTokenSource()

  let worker = async {
    while not (cts.Token.IsCancellationRequested) do
      try
        let! ctx = ctx
        Async.Start (handle ctx)
      with ex ->
        printfn "%A" ex }

  printfn "Listening to=%s" uriPrefix

  let close () =
    printfn "Closing HttpListener=%s" uriPrefix
    (listener :> IDisposable).Dispose()

  async.TryFinally(worker, close)