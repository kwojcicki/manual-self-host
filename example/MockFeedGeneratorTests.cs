using DeepEqual.Syntax;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using System.Net;
using Xunit;

namespace httpclientestdouble.example
{
    public class MockFeedGeneratorTests
    {
        private readonly FeedGenerator generator;

        public MockFeedGeneratorTests()
        {
            generator = new FeedGenerator();
        }

        [Fact]
        public async Task GetFeed_EmptyList()
        {
            var mockMessageHandler = SetupMockHttpHandler(new List<Follower>(), new List<List<Post>>());

            var feed = await generator.GetFeed(new HttpClient(mockMessageHandler.Object) { BaseAddress = new Uri("http://localhost") });

            Assert.Empty(feed);
        }

        [Fact]
        public async Task GetFeed_1Follower()
        {
            var followers = new List<Follower>() { new Follower(new Guid()) };
            var posts = new List<List<Post>>() {
                new List<Post>(){ new Post("test", DateTimeOffset.UtcNow) },
            };

            var mockMessageHandler = SetupMockHttpHandler(followers, posts);

            var feed = await generator.GetFeed(new HttpClient(mockMessageHandler.Object) { BaseAddress = new Uri("http://localhost") });

            posts[0].ShouldDeepEqual(feed);
        }


        [Fact]
        public async Task GetFeed_MultipleFollowers()
        {
            var followers = new List<Follower>() { new Follower(Guid.NewGuid()), new Follower(Guid.NewGuid()), new Follower(Guid.NewGuid()) };
            var now = DateTimeOffset.UtcNow;
            var posts = new List<List<Post>>() {
                new List<Post>(){ new Post("test", now.AddHours(1)) },
                new List<Post>(){ new Post("test", now) },
                new List<Post>(){ new Post("test", now.AddHours(0.5)) },
            };

            var mockMessageHandler = SetupMockHttpHandler(followers, posts);

            var feed = await generator.GetFeed(new HttpClient(mockMessageHandler.Object) { BaseAddress = new Uri("http://localhost") });

            posts[0][0].ShouldDeepEqual(feed[0]);
            posts[1][0].ShouldDeepEqual(feed[2]);
            posts[2][0].ShouldDeepEqual(feed[1]);
        }

        [Fact]
        public async Task GetFeed_MultiplePostsMultipleFollowers()
        {
            var followers = new List<Follower>() { new Follower(Guid.NewGuid()), new Follower(Guid.NewGuid()) };
            var now = DateTimeOffset.UtcNow;
            var posts = new List<List<Post>>() {
                new List<Post>(){ new Post("test", now.AddHours(3)), new Post("test", now.AddHours(2)), new Post("test", now.AddHours(-4)) },
                new List<Post>(){ new Post("test", now), new Post("test", now.AddHours(-1)), new Post("test", now.AddHours(-2)) }
            };

            var mockMessageHandler = SetupMockHttpHandler(followers, posts);

            var feed = await generator.GetFeed(new HttpClient(mockMessageHandler.Object) { BaseAddress = new Uri("http://localhost") });

            var expectedPosts = new List<Post>() { posts[0][0], posts[0][1], posts[1][0], posts[1][1], posts[1][2], posts[0][2] };

            expectedPosts.ShouldDeepEqual(feed);
        }

        private static Mock<HttpMessageHandler> SetupMockHttpHandler(List<Follower> followers, List<List<Post>> posts)
        {
            Assert.Equal(followers.Count, posts.Count);

            var mockMessageHandler = new Mock<HttpMessageHandler>();
            mockMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
                {
                    if (request.RequestUri?.PathAndQuery.Equals("/followers/mine") ?? false)
                    {
                        return new HttpResponseMessage
                        {
                            StatusCode = HttpStatusCode.OK,
                            Content = new StringContent(JsonConvert.SerializeObject(followers))
                        };
                    }

                    for (var i = 0; i < followers.Count; i++)
                    {
                        if (request.RequestUri?.PathAndQuery.Equals($"/{followers[i].Id}/posts") ?? false)
                        {
                            return new HttpResponseMessage
                            {
                                StatusCode = HttpStatusCode.OK,
                                Content = new StringContent(JsonConvert.SerializeObject(posts[i]))
                            };
                        }
                    }

                    return new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        Content = new StringContent("")
                    };
                });

            return mockMessageHandler;
        }
    }
}