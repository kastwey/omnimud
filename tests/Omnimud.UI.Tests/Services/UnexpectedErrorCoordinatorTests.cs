using NSubstitute;
using Omnimud.Core.Reports;
using Omnimud.UI.Services;

namespace Omnimud.UI.Tests.Services;

/// <summary>The rules of the global error handler, without a single window.</summary>
public sealed class UnexpectedErrorCoordinatorTests
{
    private sealed class FakeView : IUnexpectedErrorView
    {
        private readonly List<string> _errors = [];
        public Func<FakeView, UnexpectedErrorChoice> OnShow { get; set; } = _ => UnexpectedErrorChoice.Continue;
        public Action<string>? OnAdd { get; set; }
        public int ShownOnThread { get; private set; }
        public bool Disposed { get; private set; }
        public IReadOnlyList<string> Errors => _errors;
        public string Details => string.Join("\n----\n", _errors);

        public void AddError(string details)
        {
            OnAdd?.Invoke(details);
            _errors.Add(details);
        }

        public UnexpectedErrorChoice ShowModal()
        {
            ShownOnThread = Environment.CurrentManagedThreadId;
            return OnShow(this);
        }

        public void Dispose() => Disposed = true;
    }

    /// <summary>A "UI thread": a context that runs everything posted to it on one dedicated thread.</summary>
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?, ManualResetEventSlim?)> _queue = [];
        private readonly Thread _thread;

        public SingleThreadContext()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state, done) in _queue.GetConsumingEnumerable())
                {
                    callback(state);
                    done?.Set();
                }
            }) { IsBackground = true };
            _thread.Start();
        }

        public int ThreadId => _thread.ManagedThreadId;
        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state, null));

        public override void Send(SendOrPostCallback d, object? state)
        {
            using var done = new ManualResetEventSlim();
            _queue.Add((d, state, done));
            done.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        }

        public void Drain() => Send(_ => { }, null);
        public void Dispose() => _queue.CompleteAdding();
    }

    private readonly List<FakeView> _views = [];
    private readonly List<string> _fallbacks = [];
    private readonly List<(Exception Exception, string Details)> _reports = [];

    private UnexpectedErrorCoordinator Create(Func<FakeView>? factory = null, SynchronizationContext? context = null, Action<string>? fallback = null) =>
        new(() =>
            {
                var view = (factory ?? (() => new FakeView()))();
                _views.Add(view);
                return view;
            },
            fallback ?? _fallbacks.Add, (exception, details) => _reports.Add((exception, details)), () => context,
            new ReportSanitizer("maria", @"C:\Users\maria", "EQUIPO"));

    // ── The ordinary case ──────────────────────────────────────────────────

    [Fact]
    public void AnError_ShowsOneDialogWithItsDetails_AndTheApplicationCarriesOn()
    {
        var coordinator = Create();

        coordinator.Handle(new InvalidOperationException("algo falló"));

        var view = _views.Should().ContainSingle().Subject;
        view.Errors.Should().ContainSingle().Which.Should().Contain("InvalidOperationException").And.Contain("algo falló");
        view.Disposed.Should().BeTrue();
        coordinator.IsShowing.Should().BeFalse();
        _fallbacks.Should().BeEmpty();
        _reports.Should().BeEmpty("the user chose to continue");
    }

    [Fact]
    public void Details_NeverCarryTheUserProfile()
    {
        Create().Handle(new IOException(@"No se pudo abrir C:\Users\maria\omnimud\data\omnimud.db"));

        _views.Single().Details.Should().Contain(@"%USERPROFILE%\omnimud\data\omnimud.db").And.NotContainEquivalentOf("maria");
    }

    [Fact]
    public void ChoosingReport_OpensTheReportDialogWithThatException_AfterTheErrorDialogIsGone()
    {
        var coordinator = Create(() => new FakeView { OnShow = _ => UnexpectedErrorChoice.Report });
        var exception = new InvalidOperationException("algo falló");

        coordinator.Handle(exception);

        var report = _reports.Should().ContainSingle().Subject;
        report.Exception.Should().BeSameAs(exception);
        report.Details.Should().Contain("algo falló");
        _views.Single().Disposed.Should().BeTrue();
    }

    [Fact]
    public void AfterOneDialogIsClosed_TheNextErrorGetsANewOne()
    {
        var coordinator = Create();

        coordinator.Handle(new InvalidOperationException("uno"));
        coordinator.Handle(new InvalidOperationException("dos"));

        _views.Should().HaveCount(2);
        _views[1].Errors.Should().ContainSingle().Which.Should().Contain("dos");
    }

    // ── Never in cascade ───────────────────────────────────────────────────

    [Fact]
    public void ErrorsWhileTheDialogIsOpen_GoIntoItsDetails_InsteadOfOpeningMore()
    {
        UnexpectedErrorCoordinator coordinator = null!;
        coordinator = Create(() => new FakeView
        {
            OnShow = view =>
            {
                // What a modal message loop does: more code runs, and fails, while the dialog is up.
                coordinator.IsShowing.Should().BeTrue();
                coordinator.Handle(new FormatException("segundo"));
                coordinator.Handle(new TimeoutException("tercero"));
                return UnexpectedErrorChoice.Report;
            }
        });

        coordinator.Handle(new InvalidOperationException("primero"));

        var view = _views.Should().ContainSingle("there is never a cascade of dialogs").Subject;
        view.Errors.Should().HaveCount(3);
        view.Errors[1].Should().Contain("segundo");
        view.Errors[2].Should().Contain("tercero");
        _reports.Single().Details.Should().Contain("primero").And.Contain("segundo").And.Contain("tercero");
        _reports.Single().Exception.Message.Should().Be("primero");
    }

    // ── When the dialog itself fails ───────────────────────────────────────

    [Fact]
    public void IfTheDialogCannotBeCreated_AMessageBoxIsUsed()
    {
        var coordinator = Create(() => throw new InvalidOperationException("no hay ventana"));

        coordinator.Handle(new InvalidOperationException("el error original"));

        _fallbacks.Should().ContainSingle().Which.Should().Be("el error original");
        coordinator.IsShowing.Should().BeFalse("the next error must not think a dialog is still open");
    }

    [Fact]
    public void IfTheDialogFailsWhileShowing_AMessageBoxIsUsed_AndItIsDisposed()
    {
        var coordinator = Create(() => new FakeView { OnShow = _ => throw new InvalidOperationException("se rompió el diálogo") });

        coordinator.Handle(new InvalidOperationException("el error original"));

        _fallbacks.Should().ContainSingle().Which.Should().Be("el error original");
        _views.Single().Disposed.Should().BeTrue();
        _reports.Should().BeEmpty();
    }

    [Fact]
    public void IfEvenTheMessageBoxFails_NothingEscapes()
    {
        var coordinator = Create(() => throw new InvalidOperationException("sin diálogo"), fallback: _ => throw new InvalidOperationException("sin message box"));

        var act = () => coordinator.Handle(new InvalidOperationException("x"));

        act.Should().NotThrow();
        coordinator.IsShowing.Should().BeFalse();
    }

    [Fact]
    public void IfAddingASecondErrorFails_TheFirstDialogSurvives()
    {
        UnexpectedErrorCoordinator coordinator = null!;
        var adds = 0;
        coordinator = Create(() => new FakeView
        {
            OnAdd = _ => { if (++adds > 1) throw new InvalidOperationException("no cabe"); },
            OnShow = _ => { coordinator.Handle(new FormatException("segundo")); return UnexpectedErrorChoice.Continue; }
        });

        var act = () => coordinator.Handle(new InvalidOperationException("primero"));

        act.Should().NotThrow();
        _views.Should().ContainSingle();
        _fallbacks.Should().BeEmpty();
    }

    [Fact]
    public void IfTheReportDialogFails_AMessageBoxIsUsed()
    {
        var coordinator = new UnexpectedErrorCoordinator(() => new FakeView { OnShow = _ => UnexpectedErrorChoice.Report }, _fallbacks.Add,
            (_, _) => throw new InvalidOperationException("no se abre el informe"), () => null);

        var act = () => coordinator.Handle(new InvalidOperationException("x"));

        act.Should().NotThrow();
        _fallbacks.Should().ContainSingle();
    }

    [Fact]
    public void ANullException_IsIgnored()
    {
        Create().Handle(null!);
        _views.Should().BeEmpty();
    }

    // ── Threads ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FromAnotherThread_TheDialogIsShownOnTheUiThread_AndTheCallerDoesNotWait()
    {
        using var ui = new SingleThreadContext();
        using var release = new ManualResetEventSlim();
        var coordinator = Create(() => new FakeView { OnShow = _ => { release.Wait(TimeSpan.FromSeconds(10)); return UnexpectedErrorChoice.Continue; } }, ui);

        var worker = Task.Run(() => coordinator.Handle(new InvalidOperationException("desde otro hilo")));
        // Handle returns at once off the UI thread, although the dialog is still "open".
        await worker.WaitAsync(TimeSpan.FromSeconds(5));

        release.Set();
        ui.Drain();
        _views.Should().ContainSingle().Which.ShownOnThread.Should().Be(ui.ThreadId);
    }

    [Fact]
    public async Task HandleAndWait_BlocksUntilTheUserHasSeenIt()
    {
        using var ui = new SingleThreadContext();
        var coordinator = Create(context: ui);

        var worker = Task.Run(() => coordinator.HandleAndWait(new InvalidOperationException("hilo que muere")));

        await worker.WaitAsync(TimeSpan.FromSeconds(10));
        _views.Should().ContainSingle().Which.Disposed.Should().BeTrue("when HandleAndWait returns the dialog has already been dismissed");
        _views.Single().ShownOnThread.Should().Be(ui.ThreadId);
    }

    [Fact]
    public void ManyThreadsFailingAtOnce_NeverShowTwoDialogsAtTheSameTime()
    {
        using var ui = new SingleThreadContext();
        int active = 0, maxActive = 0;
        var coordinator = Create(() => new FakeView
        {
            OnShow = _ =>
            {
                maxActive = Math.Max(maxActive, Interlocked.Increment(ref active));
                Thread.Sleep(2);
                Interlocked.Decrement(ref active);
                return UnexpectedErrorChoice.Continue;
            }
        }, ui);

        Parallel.For(0, 20, i => coordinator.Handle(new InvalidOperationException($"error {i}")));
        ui.Drain();

        maxActive.Should().Be(1);
        _views.Should().NotBeEmpty().And.OnlyContain(v => v.Disposed && v.ShownOnThread == ui.ThreadId);
        _views.Sum(v => v.Errors.Count).Should().Be(20, "no error is lost");
        coordinator.IsShowing.Should().BeFalse();
    }

    [Fact]
    public void WhenTheUiThreadIsGone_TheMessageBoxIsUsedFromTheCallingThread()
    {
        var dead = Substitute.For<SynchronizationContext>();
        dead.When(c => c.Post(Arg.Any<SendOrPostCallback>(), Arg.Any<object>())).Do(_ => throw new InvalidOperationException("the loop has ended"));
        var coordinator = Create(context: dead);

        var act = () => coordinator.Handle(new InvalidOperationException("tarde"));

        act.Should().NotThrow();
        _fallbacks.Should().ContainSingle().Which.Should().Be("tarde");
    }

    [Fact]
    public void WithoutAUiThreadYet_TheDialogIsShownWhereTheErrorHappened()
    {
        var coordinator = Create(context: null);

        coordinator.Handle(new InvalidOperationException("durante el arranque"));

        _views.Should().ContainSingle().Which.ShownOnThread.Should().Be(Environment.CurrentManagedThreadId);
    }
}
