using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace httpclientestdouble.example
{
    public class Follower
    {
        public Guid Id { get; }

        public Follower(Guid Id)
        {
            this.Id = Id;
        }
    }

    public class Post
    {
        public string Content { get; }
        public DateTimeOffset Timestamp { get; }

        public Post(string Content, DateTimeOffset Timestamp)
        {
            this.Content = Content;
            this.Timestamp = Timestamp;
        }
    }

    public class FeedGenerator
    {
        public async Task<List<Post>> GetFeed(HttpClient client)
        {
            PriorityQueue<(Post, int, int), Post> queue = new(Comparer<Post>.Create((a, b) =>
            {
                return b.Timestamp.CompareTo(a.Timestamp);
            }));
            List<List<Post>> followersPosts = new();
            List<Post> feed = new();

            var followers = JsonConvert.DeserializeObject<List<Follower>>(await (await client.GetAsync($"/followers/mine")).Content.ReadAsStringAsync());

            // merging k sorted lists https://leetcode.com/problems/merge-k-sorted-lists/
            foreach (var follower in followers!)
            {
                var posts = JsonConvert.DeserializeObject<List<Post>>(await (await client.GetAsync($"/{follower.Id}/posts")).Content.ReadAsStringAsync());
                if (posts == null || posts.Count == 0) continue;

                queue.Enqueue((posts[0], followersPosts.Count, 0), posts[0]);
                followersPosts.Add(posts);
            }

            while (queue.Count > 0)
            {
                var post = queue.Dequeue();
                feed.Add(post.Item1);

                if (followersPosts[post.Item2].Count > post.Item3 + 1)
                {
                    var nextPost = followersPosts[post.Item2][post.Item3 + 1];
                    queue.Enqueue((nextPost, post.Item2, post.Item3 + 1), nextPost);
                }
            }

            return feed;
        }
    }

    public interface IPostsService
    {
        Task<List<Post>> GetUsersPosts(Guid id, int pageSize = -1, int page = -1);
    }

    public interface IPostsRepository
    {
        Task<List<Post>> GetPostsbyId(Guid id);
    }

    public class InMemoryPostsRepository : IPostsRepository
    {
        private readonly IReadOnlyDictionary<Guid, List<Post>> posts;

        public InMemoryPostsRepository(IReadOnlyDictionary<Guid, List<Post>> posts)
        {
            this.posts = posts;
        }

        public Task<List<Post>> GetPostsbyId(Guid id)
        {
            return Task.FromResult(posts[id]);
        }
    }

    public class PostsService : IPostsService
    {
        private readonly IPostsRepository postsRepository;

        public PostsService(IPostsRepository postsRepository)
        {
            this.postsRepository = postsRepository;
        }

        public async Task<List<Post>> GetUsersPosts(Guid id, int pageSize = -1, int page = -1)
        {
            List<Post> posts = await postsRepository.GetPostsbyId(id);

            if (pageSize == -1)
            {
                return posts;
            }

            if (page < 0 || pageSize < 0)
            {
                throw new ArgumentException($"{nameof(page)}: {page} and {nameof(pageSize)}: {pageSize} if specified must be bigger than 0.");
            }

            if (pageSize * page >= posts.Count)
            {
                throw new ArgumentException($"{nameof(page)}: {page} and {nameof(pageSize)}: {pageSize} request for a non existant page.");
            }

            return posts.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }
    }

    public interface IFollowerRepository
    {
        Task<List<Follower>> GetMyFollowers();
    }

    public class InMemoryFollowerRepository : IFollowerRepository
    {
        private readonly List<Follower> followers;

        public InMemoryFollowerRepository(List<Follower> followers)
        {
            this.followers = followers;
        }

        public Task<List<Follower>> GetMyFollowers()
        {
            return Task.FromResult(followers);
        }
    }

    [ApiController]
    [Produces("application/json")]
    [Route("/{id}/posts")]
    [Authorize]
    public class PostsController : ControllerBase
    {
        private readonly IPostsService postsService;

        public PostsController(IPostsService postsService)
        {
            this.postsService = postsService;
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<List<Post>> GetPostsById(Guid id)
        {
            return await postsService.GetUsersPosts(id);
        }
    }

    [ApiController]
    [Produces("application/json")]
    [Route("/")]
    [Authorize]
    public class FollowersController : ControllerBase
    {
        private readonly IFollowerRepository followerRepository;

        public FollowersController(IFollowerRepository followerRepository)
        {
            this.followerRepository = followerRepository;
        }

        [HttpGet("/followers/mine")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<List<Follower>> GetMyFollowers()
        {
            return await followerRepository.GetMyFollowers();
        }
    }
}
