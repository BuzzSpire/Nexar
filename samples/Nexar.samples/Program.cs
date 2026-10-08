using Nexar;

using var client = NexarClient.Builder()
    .BaseUrl("https://jsonplaceholder.typicode.com")
    .Timeout(TimeSpan.FromSeconds(10))
    .UserAgent("Nexar-samples/3.0")
    .Retry(maxRetries: 2, delay: TimeSpan.FromMilliseconds(250))
    .Build();

// GET + JSON, written as one chain
var post = await client.Get("/posts/1")
    .Send()
    .ErrorForStatus()
    .Json<Post>();
Console.WriteLine($"GET /posts/1 -> {post.Title}");

// Query parameters
var posts = await client.Get("/posts")
    .Query("userId", 1)
    .Send()
    .ErrorForStatus()
    .Json<List<Post>>();
Console.WriteLine($"GET /posts?userId=1 -> {posts.Count} posts");

// POST with a JSON body, inspecting the response before reading it
using (var created = await client.Post("/posts")
    .Json(new Post(0, 1, "Hello", "Sent with Nexar"))
    .Send())
{
    Console.WriteLine($"POST /posts -> {created.Status}, id {(await created.Json<Post>()).Id}");
}

// 4xx/5xx are normal responses until you call ErrorForStatus()
using (var missing = await client.Get("/posts/999999").Send())
{
    Console.WriteLine($"GET /posts/999999 -> {missing.Status} (IsSuccess: {missing.IsSuccess})");
}

// Errors
try
{
    await client.Get("/posts/999999").Send().ErrorForStatus();
}
catch (NexarException e) when (e.IsStatus)
{
    Console.WriteLine($"ErrorForStatus -> {e.Kind}: {e.StatusCode}");
}

record Post(int Id, int UserId, string Title, string Body);
