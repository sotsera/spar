---
title: Devtools for agents and scripts
description: Look at and drive a running Development build of a Spar app from the command line, with the spar tool.
---

# Devtools for agents and scripts

A Development build of a Spar app (a Debug build: `dotnet run`, `dotnet watch`, or F5 in an IDE) carries devtools: a small set of commands that look at the running app and drive its page from outside, for a coding agent or a script. They list the page's elements, click and type into them, wait for them, take a screenshot, run the app's commands directly, and read what the app logged and which commands the page called. You reach them through the `spar` tool in the app's folder:

```sh
dotnet spar tree
```

The `spar` tool comes with a new app's local tool manifest; run `dotnet tool restore` once first (see [Getting started](getting-started.md), section 8).

To rebuild and restart the app on request, with the build's errors in the answer, run it under [`spar dev`](spar-dev.md); the devtools work against each new run.

## What they can reach, and what they cannot

- **Only a Development build has them.** A Release build, and so every published app, carries none of their code: the build turns them off and the trimmer removes them. A test project's builds do not have them either.
- **Only your user can reach them.** The running app listens on a local socket that only the user who started it can open: a named pipe on Windows, a Unix socket in your runtime folder on Linux. It opens no network port, so a page in a browser cannot reach it.
- **Each run has a token.** The app writes the socket's address and a new token to `obj/spar-devtools.json` in its project, a file only you can read, and removes it when it closes. A tool that cannot read the token cannot use the socket.
- **Leave them out** of a project's Development builds with `<SparDevtools>false</SparDevtools>` in the project file.

## The commands

Every command talks to the running Development build of the app in the current folder; `--project <path>` names another one. Each prints short lines of text; `--json` prints the app's answer as JSON instead.

| Command | What it does |
|---|---|
| `spar tree [--limit N]` | The page's elements as assistive technology sees them: role, accessible name, text, value and state, indented by nesting, each with a reference (`e12`) for the commands below. A password field's value is never shown. |
| `spar click <target>` | Waits until the element is visible, enabled and not covered, clicks it, then lists the app's commands that ran from the click until the app was quiet again (300 ms with no call running, 3 s at most). A command the page was already running for something else can appear there too. |
| `spar fill <target> <text>` | Replaces the text of a text field, text area or editable element. |
| `spar wait <target> [--state S]` | Waits until the element is visible, or `hidden`, `attached` or `detached`. |
| `spar screenshot [--out file]` | The page as PNG, its page-drawn title bar included; by default into `obj/spar-devtools/`. |
| `spar contracts` | The app's commands and its contract snapshot: every command with its request, result and error types, and every record. |
| `spar call <contract.command> [json]` | Runs one of the app's commands, as the page would, and shows its value, its declared error, or its failure. |
| `spar calls [--limit N]` | The last 200 calls the app ran: from the page, from tests, and from `spar call`, each with its time and outcome. |
| `spar logs [--since 30s] [--source S] [--level L]` | The backend's log, the page's console messages and errors, and Content Security Policy violations, merged in time order. `--source` is `backend`, `page` or `csp`; `--level` is `debug`, `info` (the default), `warn` or `error`. |
| `spar version` | The run, the build (variant, git revision, uncommitted changes, contracts' fingerprint) and the host. |

### Reading the tree

Each line of `spar tree` is one element: its reference, its role, its accessible name in quotes where it has one, then after a colon the text it shows where its name does not already say it, then a field's value after `=` and its states in brackets. A form that has just greeted someone reads:

```text
MyApp — https://app.localhost/ (document 1)
e1 document
  e2 main
    e3 heading "MyApp" [level 1]
    e4 form
      e5 textbox "Name" = "Ada"
      e6 button "Greet" [focused]
    e7 status: "Hello, Ada!"
```

An element's text is what it shows outside the elements listed under it: a status line's message, a paragraph, a list item, the loose text of a section. A link or an emphasis inside a sentence is listed under the sentence, and its words stay in the sentence's text. Text that labels another element, such as a field's label or a group's legend, appears only as that element's name, and the content of a button, a checkbox or an image is its name. Hidden text is never shown, and neither is a field's value as text. A text or a value longer than 200 characters is cut and ends with ` …`. With `--json`, each element has `text` beside `name` when it has one.

A **target** is a reference from the last `spar tree` of the page, such as `e12`, or a locator:

- `--role button --name "Save"`: by role and accessible name;
- `--text "Save"`, `--label "Title"`, `--placeholder "Search"`, `--alt "Logo"`, `--title "Close"`;
- `--testid save-button`: the element's `data-testid`;
- `--css ".toolbar button"`: a CSS selector.

A name or text matches as a case-insensitive part unless `--exact` is given. When a locator matches several elements, the command refuses and lists them; `--nth 0` takes the first. A reference is valid for the page's current document: after the page navigates or reloads, take `spar tree` again.

For example, creating a page in a notebook app:

```sh
dotnet spar tree
dotnet spar click --role button --name "New page" --nth 0
dotnet spar fill --label Title "Ideas"
dotnet spar click --role button --name "Create page"
dotnet spar calls --limit 5
```

The last click prints the commands that ran while it did, such as `pages.create success 4.8 ms` and `pages.list success 5.1 ms`.

## Clicks and typing on each system

- **On Windows** a click and typing are the browser's own trusted input, as from a mouse and a keyboard.
- **On Linux** they are events the page dispatches to itself, because there is no input that reaches only the app's window on your desktop. Most pages behave the same, but code that checks `event.isTrusted` sees `false`, and `spar click` says so in its answer.
- **With the Loopback launch profile** the page runs in a browser, so `tree`, `click`, `fill`, `wait` and `screenshot` refuse; `contracts`, `call`, `calls`, `logs` and `version` work.

## Waiting and time limits

`click`, `fill` and `wait` wait 5 seconds for their element by default, and `call` 30 seconds for its command; `--timeout <milliseconds>` changes it. A time limit that runs out means unfinished, not failed: a command that `spar call` stopped waiting for may still be running, and `spar calls` shows it when it ends.

## Exit codes and errors

The exit code is 0 when the command did what it said, 2 when the app refused it or no Development run of the app is listening, and 64 for a wrong command line. A refusal names its reason:

| Reason | Meaning |
|---|---|
| `not_found` | No such command, or no such tool. |
| `ambiguous` | The target matches several elements; they are listed. |
| `stale_target` | The reference is not from the page's last tree, or the run changed; take `spar tree` again. |
| `timeout` | Unfinished within the time limit. |
| `no_page` | The window has no page yet, or is closing. |
| `unavailable` | The page tools need the app's own window (Loopback). |
| `invalid` | The request is wrong, for example `spar logs --since yesterday`. |

## From an MCP client

An agent that works through [MCP](https://modelcontextprotocol.io/) tools rather than a shell can reach the same commands through `spar mcp`, an MCP server over standard input and output. Its tools are the commands above, plus `reload` (from [`spar dev`](spar-dev.md)), `snap` and `doctor`; each runs exactly what the `spar` command would, and `screenshot` also returns the image. It is opt-in: configure your MCP client to start it in the app's folder, for example in a project's `.mcp.json`:

```json
{
  "mcpServers": {
    "spar": { "command": "dotnet", "args": ["spar", "mcp"] }
  }
}
```

The server finds the running Development build the same way the commands do, so start the app first (`dotnet spar dev` in the background, or `dotnet run`). A tool's arguments are the command's options as JSON, for example `{"target": {"role": "button", "name": "Save"}}` for `click`, and a command that fails returns an error result with the command's message.

The command line is the main way in: it needs no configuration and costs an agent nothing until it runs a command, while an MCP client loads every tool's description into each session.

## For agents and other tools: the protocol

The `spar` commands are one way in. Any program can use the same socket: read `obj/spar-devtools.json` (`transport` is `pipe` or `unix`, `address` the pipe's name or the socket's path, `token` the run's token, `run` the run's id), connect, and send newline-delimited [JSON-RPC 2.0](https://www.jsonrpc.org/specification), one request per line:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"token":"…"}}
{"jsonrpc":"2.0","id":2,"method":"tree","params":{"run":"5177cff9"}}
```

The first request of a connection is `initialize` with the token; a wrong one ends the connection. The method names are the command names, and each request's parameters are the command's options (`target`, `text`, `timeout`, `limit`, `since`, …). A request may name the `run` it is for, and gets `stale_target` when that run has ended. An id is used once per connection; a repeated one is refused and never run twice. A refusal is a JSON-RPC error whose `data.reason` is one of the reasons above.
