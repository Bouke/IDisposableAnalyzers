﻿namespace IDisposableAnalyzers.Test.IDISP006ImplementIDisposableTests
{
    using Gu.Roslyn.Asserts;
    using NUnit.Framework;

    public static partial class CodeFix
    {
        public static class OverrideDispose
        {
            private static readonly FieldAndPropertyDeclarationAnalyzer Analyzer = new();

            [Test]
            public static void SubclassStreamReader()
            {
                var before = @"
namespace N
{
    using System.IO;

    public class C : StreamReader
    {
        ↓private readonly Stream stream = File.OpenRead(string.Empty);

        public C(string path)
            : base(path)
        {
        }
    }
}";

                var after = @"
namespace N
{
    using System.IO;

    public class C : StreamReader
    {
        private readonly Stream stream = File.OpenRead(string.Empty);
        private bool disposed;

        public C(string path)
            : base(path)
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            if (disposing)
            {
            }

            base.Dispose(disposing);
        }

        protected virtual void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new System.ObjectDisposedException(this.GetType().FullName);
            }
        }
    }
}";
                RoslynAssert.CodeFix(Analyzer, Fix, ExpectedDiagnostic, before, after);
                RoslynAssert.FixAll(Analyzer, Fix, ExpectedDiagnostic, before, after);
            }

            [Test]
            public static void WhenBaseHasThrowIfDisposed()
            {
                var baseClass = @"
namespace N
{
    using System;

    public class BaseClass : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            this.Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            if (disposing)
            {
            }
        }

        protected virtual void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}";
                var before = @"
namespace N
{
    using System.IO;

    public class C : BaseClass
    {
        ↓private readonly Stream stream = File.OpenRead(string.Empty);
    }
}";

                var after = @"
namespace N
{
    using System.IO;

    public class C : BaseClass
    {
        private readonly Stream stream = File.OpenRead(string.Empty);
        private bool disposed;

        protected override void Dispose(bool disposing)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            if (disposing)
            {
            }

            base.Dispose(disposing);
        }
    }
}";
                RoslynAssert.CodeFix(Analyzer, Fix, ExpectedDiagnostic, new[] { baseClass, before }, after);
                RoslynAssert.FixAll(Analyzer, Fix, ExpectedDiagnostic, new[] { baseClass, before }, after);
            }

            [Test]
            public static void WhenBaseHasPublicVirtualDisposeAndThrowIfDisposed()
            {
                var baseClass = @"
namespace N
{
    using System;

    public class BaseClass : IDisposable
    {
        private bool disposed;

        public virtual void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
        }

        protected virtual void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}";
                var before = @"
namespace N
{
    using System.IO;

    public class C : BaseClass
    {
        ↓private readonly Stream stream = File.OpenRead(string.Empty);
    }
}";

                var after = @"
namespace N
{
    using System.IO;

    public class C : BaseClass
    {
        private readonly Stream stream = File.OpenRead(string.Empty);
        private bool disposed;

        public override void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            base.Dispose();
        }
    }
}";
                RoslynAssert.CodeFix(Analyzer, Fix, ExpectedDiagnostic, new[] { baseClass, before }, after);
                RoslynAssert.FixAll(Analyzer, Fix, ExpectedDiagnostic, new[] { baseClass, before }, after);
            }

            [Test]
            public static void UnderscoreWhenThrowIsNotVirtual()
            {
                var baseClass = @"
namespace N
{
    using System;

    public class BaseClass : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (disposing)
            {
            }
        }

        protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}";
                var before = @"
namespace N
{
    using System.IO;

    public class C : BaseClass
    {
        ↓private readonly Stream _stream = File.OpenRead(string.Empty);
    }
}";

                var after = @"
namespace N
{
    using System.IO;

    public class C : BaseClass
    {
        private readonly Stream _stream = File.OpenRead(string.Empty);
        private bool _disposed;

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (disposing)
            {
            }

            base.Dispose(disposing);
        }
    }
}";
                RoslynAssert.CodeFix(Analyzer, Fix, ExpectedDiagnostic, new[] { baseClass, before }, after);
                RoslynAssert.FixAll(Analyzer, Fix, ExpectedDiagnostic, new[] { baseClass, before }, after);
            }

            [Test]
            public static void UnderscoreWhenThrowIsVirtual()
            {
                var baseClass = @"
namespace N
{
    using System;

    public class BaseClass : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (disposing)
            {
            }
        }

        protected virtual void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}";
                var before = @"
namespace N
{
    using System.IO;

    public class C : BaseClass
    {
        ↓private readonly Stream _stream = File.OpenRead(string.Empty);
    }
}";

                var after = @"
namespace N
{
    using System.IO;

    public class C : BaseClass
    {
        private readonly Stream _stream = File.OpenRead(string.Empty);
        private bool _disposed;

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (disposing)
            {
            }

            base.Dispose(disposing);
        }
    }
}";
                RoslynAssert.CodeFix(Analyzer, Fix, ExpectedDiagnostic, new[] { baseClass, before }, after);
                RoslynAssert.FixAll(Analyzer, Fix, ExpectedDiagnostic, new[] { baseClass, before }, after);
            }

            [Test]
            public static void SubclassingNinjectModule()
            {
                var before = @"
namespace N
{
    using System;
    using Ninject.Modules;

    internal class C : NinjectModule
    {
        ↓private readonly IDisposable disposable = new Disposable();

        public override void Load()
        {
            throw new NotImplementedException();
        }
    }
}";

                var after = @"
namespace N
{
    using System;
    using Ninject.Modules;

    internal class C : NinjectModule
    {
        private readonly IDisposable disposable = new Disposable();
        private bool disposed;

        public override void Load()
        {
            throw new NotImplementedException();
        }

        public override void Dispose(bool disposing)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            if (disposing)
            {
            }

            base.Dispose(disposing);
        }

        protected virtual void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(this.GetType().FullName);
            }
        }
    }
}";
                RoslynAssert.CodeFix(Analyzer, Fix, ExpectedDiagnostic, new[] { Disposable, before }, after);
                RoslynAssert.FixAll(Analyzer, Fix, ExpectedDiagnostic, new[] { Disposable, before }, after);
            }
        }
    }
}
