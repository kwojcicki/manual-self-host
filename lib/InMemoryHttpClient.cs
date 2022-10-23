using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using Moq.Protected;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web;

namespace httpclientestdouble.lib
{
    public class InMemoryHttpClient
    {
        private readonly IObjectDeserializer objectDeserializer;

        private static (bool, string) MatchesController(ControllerBase controller, HttpRequestMessage request)
        {
            var customAttributes = controller.GetType().GetCustomAttributes(typeof(RouteAttribute), false);
            var topLevelPath = customAttributes.Length > 0 ? ((RouteAttribute)customAttributes[0]).Template : "/";
            // turning /{id}/posts -> /(.*)/posts
            // topLevelPath = '/' + Regex.Replace(topLevelPath, "{[a-zA-Z0-9]*}", "(.*)").TrimEnd('/').TrimStart('/');
            topLevelPath = '/' + Regex.Replace(topLevelPath, "{[a-zA-Z0-9]*}", "([^\\/]*)").TrimEnd('/').TrimStart('/');
            var topLevelPathRegex = new Regex("^" + topLevelPath + ".*");

            if (!topLevelPathRegex.IsMatch(request.RequestUri?.PathAndQuery!))
            {
                return (false, "");
            }

            return (true, topLevelPath);
        }

        private static (bool, System.Text.RegularExpressions.Match) MatchesAction(HttpRequestMessage request, HttpMethodAttribute action, string topLevelPath)
        {
            bool foundMethod = false;
            foreach (var httpmethod in action.HttpMethods)
            {
                foundMethod |= httpmethod.Equals(request.Method.Method);
            }

            Regex methodRegex;
            if (string.IsNullOrEmpty(action.Template))
            {
                methodRegex = new Regex("^" + topLevelPath + "$");
            }
            else
            {
                // turning /{id}/posts -> /([^\/])/posts
                // methodRegex = new Regex("^" + topLevelPath.TrimEnd('/') + "/" + Regex.Replace(action.Template?.TrimStart('/') ?? "", "{[a-zA-Z0-9]*}", "([a-zA-Z0-9\\-]*)") + "$");
                methodRegex = new Regex("^" + topLevelPath.TrimEnd('/') + "/" + Regex.Replace(action.Template?.TrimStart('/') ?? "", "{[a-zA-Z0-9]*}", "([^\\/]*)") + "$");
            }

            var match = methodRegex.Match(request.RequestUri?.AbsolutePath!);

            return (foundMethod && match.Success, match);
        }

        private List<object?> ParseParameters(HttpRequestMessage request, MethodInfo method, System.Text.RegularExpressions.Match match)
        {

            var parameters = new List<object?>();
            var urlParameterIndex = 1; // groups start at 1
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                var required = parameter.IsDefined(typeof(BindRequiredAttribute), false);

                if (parameter.IsDefined(typeof(FromQueryAttribute), false))
                {
                    // attempt to find value in url
                    var parameterValue = HttpUtility.ParseQueryString(request.RequestUri?.Query!).Get(parameter.Name);
                    if (required && string.IsNullOrEmpty(parameterValue))
                    {
                        throw new Exception("missing required parameter");
                    }
                    else if (!string.IsNullOrEmpty(parameterValue))
                    {
                        parameters.Add(objectDeserializer.ConvertValue(parameterValue, parameter.ParameterType));
                    }
                    else
                    {
                        parameters.Add(parameter.RawDefaultValue);
                    }
                }
                else if (parameter.IsDefined(typeof(FromBodyAttribute), false))
                {
                    // parameter in the body
                    parameters.Add(objectDeserializer.ConvertValue(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult()!, parameter.ParameterType));
                }
                else
                {
                    // should be url parameter
                    parameters.Add(objectDeserializer.ConvertValue(match.Groups[urlParameterIndex].Captures[0].Value, parameter.ParameterType));
                    urlParameterIndex++;
                }
            }

            return parameters;
        }

        private static async Task<HttpResponseMessage> InvokeController(MethodInfo method, ControllerBase controller, List<object?> parameters)
        {
            if (method.Invoke(controller, parameters.ToArray()) is not Task task)
            {
                throw new Exception();
            }

            await task.ConfigureAwait(false);

            var resp = task.GetType().GetProperty("Result")?.GetValue(task);

            return new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(resp)),
            };
        }

        private async Task<HttpResponseMessage?> TryController(ControllerBase controller, HttpRequestMessage request)
        {
            var (matches, topLevelPath) = MatchesController(controller, request);

            if (!matches)
            {
                return null;
            }

            foreach (var method in controller.GetType().GetMethods())
            {
                var actionRoutes = method.GetCustomAttributes(typeof(RouteAttribute), false);
                var actions = method.GetCustomAttributes(typeof(HttpMethodAttribute), false);
                foreach (HttpMethodAttribute action in actions)
                {
                    var (matchesAction, match) = MatchesAction(request, action, topLevelPath);
                    if (!matchesAction) continue;

                    var parameters = ParseParameters(request, method, match);

                    return await InvokeController(method, controller, parameters);
                }
            }

            return null;
        }

        public InMemoryHttpClient(IObjectDeserializer? objectDeserializer)
        {
            this.objectDeserializer = objectDeserializer ?? new ObjectDeserializer();
        }

        public HttpClient GetHttpClient(ControllerBase[] controllers)
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
               .Protected()
               // Setup the PROTECTED method to mock
               .Setup<Task<HttpResponseMessage>>(
                  "SendAsync",
                  ItExpr.IsAny<HttpRequestMessage>(),
                  ItExpr.IsAny<CancellationToken>()
               )
               // prepare the expected response of the mocked http call
               .Returns(async (HttpRequestMessage request, CancellationToken _) =>
               {
                   for (int i = 0; i < controllers.Length; i++)
                   {
                       var task = TryController(controllers[i], request);

                       await task.ConfigureAwait(false);

                       var resp = task.GetType().GetProperty("Result")?.GetValue(task);
                       if (resp != null)
                       {
                           return (HttpResponseMessage)resp;
                       }
                   }

                   return new HttpResponseMessage()
                   {
                       StatusCode = HttpStatusCode.NotFound,
                       Content = new StringContent(""),
                   };
               })
               .Verifiable();

            var httpClient = new HttpClient(handlerMock.Object)
            {
                BaseAddress = new Uri("http://localhost.com/"),
            };

            return httpClient;
        }
    }
}
