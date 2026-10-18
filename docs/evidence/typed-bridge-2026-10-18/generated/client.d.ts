// Generated from the C# contract. Do not edit.
export interface CallOptions { signal?: AbortSignal; timeoutMs?: number }
export interface BridgeIdentity { version: 1; contract: string; client: string; document: string }
export declare function connect(): Promise<BridgeIdentity>;
export interface GreetingRequest {
  name: string;
}
export interface GreetingReply {
  greeting: string;
  counter: number;
  activeDelays: number;
  phase: number;
  deniedEntries: number;
}
export interface CounterRequest {
  delta: number;
}
export interface CounterReply {
  value: number;
}
export interface DelayRequest {
  milliseconds: number;
  ignoreCancellation: boolean;
}
export interface DelayReply {
  outcome: string;
}
export interface DeniedRequest {
  value: string;
}
export interface DeniedReply {
  called: boolean;
}
export interface ReportRequest {
  stage: string;
  detail: string;
}
export interface ReportReply {
  accepted: boolean;
}
export declare const backend: {
  greet(request: GreetingRequest, options?: CallOptions): Promise<GreetingReply>;
  counter(request: CounterRequest, options?: CallOptions): Promise<CounterReply>;
  delay(request: DelayRequest, options?: CallOptions): Promise<DelayReply>;
  denied(request: DeniedRequest, options?: CallOptions): Promise<DeniedReply>;
  report(request: ReportRequest, options?: CallOptions): Promise<ReportReply>;
};
