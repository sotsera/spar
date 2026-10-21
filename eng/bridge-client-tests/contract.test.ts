import { backend, connect, type GreetingReply } from '../../fixtures/Spar.NativeSample.Contracts/obj/Debug/net10.0/spar/client.js';

const identity = await connect();
const version: 1 = identity.version;
const reply: GreetingReply = await backend.greet({ name: 'Spar' });
const counter: number = (await backend.counter({ delta: 1 })).value;
await backend.delay({ milliseconds: 10, ignoreCancellation: false }, { signal: new AbortController().signal, timeoutMs: 100 });
// @ts-expect-error Missing DTO field.
await backend.greet({});
// @ts-expect-error Scalar type must match the generated C# contract.
await backend.counter({ delta: '1' });
// @ts-expect-error Unknown operations are not exported.
await backend.arbitraryShell({ command: 'anything' });
// @ts-expect-error Reply DTO fields are typed.
const invalid: boolean = reply.greeting;
void [version, counter, invalid];
