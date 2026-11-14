// Tests run inside the Angular zone (NgZone.run), as event handlers and bootstrap code do in a real app.
import { NgZone, provideZonelessChangeDetection } from "@angular/core";
import { bootstrapApplication } from "@angular/platform-browser";
import { Root, runTests } from "./tests";

void bootstrapApplication(Root, { providers: [provideZonelessChangeDetection()] }).then(appRef => appRef.injector.get(NgZone).run(() => runTests(appRef, "zoneless")));
