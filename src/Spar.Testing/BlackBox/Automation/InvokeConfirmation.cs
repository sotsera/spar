using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using Spar.Testing.BlackBox.Interop;

namespace Spar.Testing;

/// <summary>
/// The browser's confirmation that a page element's invoke reached the page: UI Automation's Invoked event on the
/// element, which Chromium raises once the page has dispatched the click. An invoke can be taken by UI Automation and
/// never reach the page (pitfalls, "A black-box Invoke right after a page button appears can do nothing"): in 30
/// measured launches the event came exactly when the click landed (28 with both, 2 with neither). It is registered on
/// the element before the invoke and removed, on the UI Automation thread, when the locator has waited for it.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class InvokeConfirmation : IInvokeConfirmation
{
    /// <summary><c>UIA_Invoke_InvokedEventId</c> (UIAutomationClient.h).</summary>
    private const int InvokedEvent = 20009;

    /// <summary><c>TreeScope_Element</c>: the element itself, not its descendants.</summary>
    private const int ScopeElement = 0x1;

    /// <summary><c>UIA_E_ELEMENTNOTAVAILABLE</c>: the element is no longer in the tree.</summary>
    private const int ElementNotAvailable = unchecked((int)0x80040201);

    private readonly AutomationHost _host;
    private readonly InvokedHandler _handler = new();

    // Owned: one reference each, from the marshaller, released at removal. UI Automation holds its own while registered.
    private nint _element;
    private nint _handlerPointer;

    private InvokeConfirmation(AutomationHost host)
    {
        _host = host;
    }

    public Task Invoked => _handler.Invoked;

    /// <summary>Registers for the element's Invoked event; on the UI Automation thread, within the call that found it.</summary>
    public static unsafe InvokeConfirmation Register(Uia uia, UiaElement element, AutomationHost host)
    {
        var confirmation = new InvokeConfirmation(host)
        {
            _element = (nint)ComInterfaceMarshaller<IUIAutomationElement>.ConvertToUnmanaged(element.Com),
        };
        confirmation._handlerPointer = (nint)ComInterfaceMarshaller<IUIAutomationEventHandler>.ConvertToUnmanaged(confirmation._handler);
        try
        {
            uia.Automation.AddAutomationEventHandler(InvokedEvent, confirmation._element, ScopeElement, 0, confirmation._handlerPointer);
        }
        catch
        {
            confirmation.Release();
            throw;
        }

        return confirmation;
    }

    /// <summary>
    /// Whether the element has left the accessibility tree: a press that removes its own element (a dialog's button
    /// that closes it, a link that leaves the page) gets no Invoked event, since the browser drops the event with the
    /// node. A lost press leaves the element where it was. On the UI Automation thread.
    /// </summary>
    public bool IsGone(Uia uia)
    {
        try
        {
            _ = uia.Borrow(_element).Name;
            return false;
        }
        catch (COMException exception) when (exception.HResult == ElementNotAvailable)
        {
            return true;
        }
    }

    public Task<bool> IsGoneAsync() => _host.IsGoneAsync(this);

    /// <summary>Removes the registration; on the UI Automation thread. A gone element's failure is ignored: nothing is left to call.</summary>
    public void Remove(Uia uia)
    {
        if (_element == 0)
        {
            return;
        }

        _ = uia.Automation.RemoveAutomationEventHandler(InvokedEvent, _element, _handlerPointer);
        Release();
    }

    public async ValueTask DisposeAsync() => await _host.RemoveAsync(this);

    private void Release()
    {
        Marshal.Release(_element);
        Marshal.Release(_handlerPointer);
        _element = 0;
        _handlerPointer = 0;
    }
}

/// <summary>The Invoked event's handler, called by UI Automation on a thread of its own with the sender borrowed.</summary>
[GeneratedComClass]
internal sealed partial class InvokedHandler : IUIAutomationEventHandler
{
    private readonly TaskCompletionSource _invoked = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Invoked => _invoked.Task;

    public void HandleAutomationEvent(nint sender, int eventId) => _invoked.TrySetResult();
}
