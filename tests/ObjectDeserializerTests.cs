using httpclientestdouble.lib;
using Xunit;

namespace httpclientestdouble.tests
{
    public class ObjectDeserializerTests
    {
        private readonly ObjectDeserializer deserializer;

        public ObjectDeserializerTests()
        {
            deserializer = new ObjectDeserializer();
        }

        private enum TestEnum
        {
            test1 = 1,
            test2 = 2,
        }

        private class TestObject
        {
            private readonly int x;

            public TestObject(int x)
            {
                this.x = x;
            }

            public override bool Equals(object? obj)
            {
                if (obj is not TestObject item)
                {
                    return false;
                }

                return x.Equals(item.x);
            }

            public override int GetHashCode()
            {
                return x.GetHashCode();
            }
        }

        [Theory]
        [InlineData("1", typeof(int), 1)]
        [InlineData("test", typeof(string), "test")]
        [InlineData("true", typeof(bool), true)]
        [InlineData("2", typeof(TestEnum), TestEnum.test2)]
        public void CanDeserialize(string value, Type outType, object actual)
        {
            var ret = deserializer.ConvertValue(value, outType);
            Assert.NotNull(ret);
            Assert.Equal(actual, ret);
        }

        [Fact]
        public void CanDeserializeObjects()
        {
            Assert.Equal(new TestObject(1), deserializer.ConvertValue("{x:1}", typeof(TestObject)));
        }
    }
}