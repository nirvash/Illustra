using NUnit.Framework;

namespace Illustra.Tests
{
    [TestFixture]
    public class SampleTests
    {
        [Test]
        public void SampleTestCase()
        {
            int actual = 1 + 1;
            Assert.That(actual, Is.EqualTo(2)); // NUnit2007, NUnit2009 Fix: Use variable for actual value and meaningful comparison
        }
    }
}
