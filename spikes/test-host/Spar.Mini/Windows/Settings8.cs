using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Windows;

// WebView2.h (1.0.4314.35): ICoreWebView2Settings8, flat: Settings 3-20, _2 21-22, _3 23-24, _4 25-28, _5 29-30,
// _6 31-32, _7 33-34, _8 35-36. Placeholders keep the offsets; they are never called. Same layout as
// spikes/webview-profiles. Reached by casting the settings object (QueryInterface).
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("9e6b0e8f-86ad-4e81-8147-a9b5edb68650")]
internal partial interface ICoreWebView2Settings8
{
    [PreserveSig] int Slot03();

    [PreserveSig] int Slot04();

    [PreserveSig] int Slot05();

    [PreserveSig] int Slot06();

    [PreserveSig] int Slot07();

    [PreserveSig] int Slot08();

    [PreserveSig] int Slot09();

    [PreserveSig] int Slot10();

    [PreserveSig] int Slot11();

    [PreserveSig] int Slot12();

    [PreserveSig] int Slot13();

    [PreserveSig] int Slot14();

    [PreserveSig] int Slot15();

    [PreserveSig] int Slot16();

    [PreserveSig] int Slot17();

    [PreserveSig] int Slot18();

    [PreserveSig] int Slot19();

    [PreserveSig] int Slot20();

    [PreserveSig] int Slot21();

    [PreserveSig] int Slot22();

    [PreserveSig] int Slot23();

    [PreserveSig] int Slot24();

    [PreserveSig] int Slot25();

    [PreserveSig] int Slot26();

    [PreserveSig] int Slot27();

    [PreserveSig] int Slot28();

    [PreserveSig] int Slot29();

    [PreserveSig] int Slot30();

    [PreserveSig] int Slot31();

    [PreserveSig] int Slot32();

    [PreserveSig] int Slot33();

    [PreserveSig] int Slot34();

    int GetIsReputationCheckingRequired();

    void PutIsReputationCheckingRequired(int value);
}
