@echo off
ver
reg query "HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" /v pv
reg query "HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\ClientState\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" /v EBWebView
dir /b "C:\Program Files (x86)\Microsoft\EdgeWebView\Application"
echo --- window test without the explicit runtime folder
set SPIKE_WEBVIEW2_BROWSER_FOLDER=
"%~dp0SampleApp.AotTests.exe" -noColor -method SampleApp.Tests.WindowTests.Page_facts
