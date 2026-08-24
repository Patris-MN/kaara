export function shouldApplyResponse(requestId: number, activeRequestId: number): boolean {
  return requestId === activeRequestId;
}
