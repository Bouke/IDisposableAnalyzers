﻿namespace IDisposableAnalyzers.Test.IDISP008DoNontMixInjectedAndCreatedForMemberTests
{
    using Gu.Roslyn.Asserts;
    using NUnit.Framework;

    // ReSharper disable once UnusedTypeParameter
    public partial class Valid<T>
    {
        public static class Recursion
        {
            [Test]
            public static void IgnoresWhenDisposingRecursiveProperty()
            {
                var testCode = @"
namespace N
{
    using System;

    public class C : IDisposable
    {
        public IDisposable RecursiveProperty => RecursiveProperty;

        public void Dispose()
        {
            this.RecursiveProperty.Dispose();
        }
    }
}";
                RoslynAssert.Valid(Analyzer, testCode);
            }

            [Test]
            public static void IgnoresWhenNotDisposingRecursiveProperty()
            {
                var testCode = @"
namespace N
{
    using System;

    public class C : IDisposable
    {
        public IDisposable RecursiveProperty => RecursiveProperty;

        public void Dispose()
        {
        }
    }
}";
                RoslynAssert.Valid(Analyzer, testCode);
            }

            [Test]
            public static void IgnoresWhenDisposingFieldAssignedWithRecursiveProperty()
            {
                var testCode = @"
namespace N
{
    using System;

    public class C : IDisposable
    {
        private IDisposable disposable;

        public C()
        {
            this.disposable = this.RecursiveProperty;
        }

        public IDisposable RecursiveProperty => RecursiveProperty;

        public void Dispose()
        {
            this.disposable.Dispose();
        }
    }
}";
                RoslynAssert.Valid(Analyzer, testCode);
            }

            [Test]
            public static void IgnoresWhenNotDisposingFieldAssignedWithRecursiveProperty()
            {
                var testCode = @"
namespace N
{
    using System;

    public class C : IDisposable
    {
        private IDisposable disposable;

        public C()
        {
            this.disposable = this.RecursiveProperty;
        }

        public IDisposable RecursiveProperty => RecursiveProperty;

        public void Dispose()
        {
        }
    }
}";
                RoslynAssert.Valid(Analyzer, testCode);
            }

            [Test]
            public static void IgnoresWhenDisposingRecursiveMethod()
            {
                var testCode = @"
namespace N
{
    using System;

    public class C
    {
        public IDisposable RecursiveMethod() => RecursiveMethod();

        public void Dispose()
        {
            this.RecursiveMethod().Dispose();
        }
    }
}";
                RoslynAssert.Valid(Analyzer, testCode);
            }
        }
    }
}