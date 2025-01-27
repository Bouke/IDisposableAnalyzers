﻿namespace IDisposableAnalyzers.Test.IDISP002DisposeMemberTests
{
    using Gu.Roslyn.Asserts;
    using NUnit.Framework;

    public static partial class CodeFix
    {
        public static class RefAndOut
        {
            private static readonly FieldAndPropertyDeclarationAnalyzer Analyzer = new();
            private static readonly DisposeMemberFix Fix = new();
            private static readonly ExpectedDiagnostic ExpectedDiagnostic = ExpectedDiagnostic.Create(Descriptors.IDISP002DisposeMember);

            [Test]
            public static void AssigningFieldViaOutParameterInCtor()
            {
                var before = @"
namespace N
{
    using System;
    using System.IO;

    public sealed class C : IDisposable
    {
        ↓private readonly Stream stream;

        public C()
        {
            if (TryGetStream(out this.stream))
            {
            }
        }

        public bool TryGetStream(out Stream outValue)
        {
            outValue = File.OpenRead(string.Empty);
            return true;
        }


        public void Dispose()
        {
        }
    }
}";

                var after = @"
namespace N
{
    using System;
    using System.IO;

    public sealed class C : IDisposable
    {
        private readonly Stream stream;

        public C()
        {
            if (TryGetStream(out this.stream))
            {
            }
        }

        public bool TryGetStream(out Stream outValue)
        {
            outValue = File.OpenRead(string.Empty);
            return true;
        }


        public void Dispose()
        {
            this.stream?.Dispose();
        }
    }
}";
                RoslynAssert.CodeFix(Analyzer, Fix, ExpectedDiagnostic, before, after);
                RoslynAssert.FixAll(Analyzer, Fix, ExpectedDiagnostic, before, after);
            }

            [Test]
            public static void AssigningFieldViaRefParameterInCtor()
            {
                var before = @"
namespace N
{
    using System;
    using System.IO;

    public sealed class C : IDisposable
    {
        ↓private readonly Stream? stream;

        public C()
        {
            if (TryGetStream(ref this.stream))
            {
            }
        }

        public bool TryGetStream(ref Stream? outValue)
        {
            outValue = File.OpenRead(string.Empty);
            return true;
        }


        public void Dispose()
        {
        }
    }
}";

                var after = @"
namespace N
{
    using System;
    using System.IO;

    public sealed class C : IDisposable
    {
        private readonly Stream? stream;

        public C()
        {
            if (TryGetStream(ref this.stream))
            {
            }
        }

        public bool TryGetStream(ref Stream? outValue)
        {
            outValue = File.OpenRead(string.Empty);
            return true;
        }


        public void Dispose()
        {
            this.stream?.Dispose();
        }
    }
}";
                RoslynAssert.CodeFix(Analyzer, Fix, ExpectedDiagnostic, before, after);
                RoslynAssert.FixAll(Analyzer, Fix, ExpectedDiagnostic, before, after);
            }
        }
    }
}
