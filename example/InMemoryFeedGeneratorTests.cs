using DeepEqual.Syntax;
using httpclientestdouble.lib;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace httpclientestdouble.example
{
    public class InMemoryFeedGeneratorTests
    {
        private readonly FeedGenerator generator;
        private readonly InMemoryHttpClient testDouble;

        public InMemoryFeedGeneratorTests()
        {
            generator = new FeedGenerator();
            testDouble = new InMemoryHttpClient(new ObjectDeserializer());
        }

        private HttpClient SetupTestDouble(List<Follower> followers, List<List<Post>> posts)
        {
            var inmemoryFollowersRepository = new InMemoryFollowerRepository(followers);
            var followerService = new FollowersService(inmemoryFollowersRepository);
            var followerController = new FollowersController(followerService);

            var postsDict = new Dictionary<Guid, List<Post>>();
            for (int i = 0; i < followers.Count; i++)
            {
                postsDict.Add(followers[i].Id, posts[i]);
            }
            var inmemoryPostRepository = new InMemoryPostsRepository(postsDict);
            var postsService = new PostsService(inmemoryPostRepository);
            var postsController = new PostsController(postsService);

            return testDouble.GetHttpClient(new ControllerBase[] { followerController, postsController });
        }

        [Fact]
        public async Task GetFeed_EmptyList()
        {
            var testDoubleMessageHandler = SetupTestDouble(new List<Follower>(), new List<List<Post>>());

            var feed = await generator.GetFeed(testDoubleMessageHandler);

            Assert.Empty(feed);
        }

        [Fact]
        public async Task GetFeed_1Follower()
        {
            var followers = new List<Follower>() { new Follower(new Guid()) };
            var posts = new List<List<Post>>() {
                new List<Post>(){ new Post("test", DateTimeOffset.UtcNow) },
            };

            var testDoubleMessageHandler = SetupTestDouble(followers, posts);

            var feed = await generator.GetFeed(testDoubleMessageHandler);

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

            var testDoubleMessageHandler = SetupTestDouble(followers, posts);

            var feed = await generator.GetFeed(testDoubleMessageHandler);

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

            var testDoubleMessageHandler = SetupTestDouble(followers, posts);

            var feed = await generator.GetFeed(testDoubleMessageHandler);

            var expectedPosts = new List<Post>() { posts[0][0], posts[0][1], posts[1][0], posts[1][1], posts[1][2], posts[0][2] };

            expectedPosts.ShouldDeepEqual(feed);
        }
    }
}