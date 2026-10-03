import { vi } from "vitest";

// The session provider ends an identity by replacing the document (kernel/deviceState.startOver).
// The test DOM cannot load a new document: record the call instead, so tests can assert it and the
// document (with all module memory) stays, which is the harder case for the client-state gate.
vi.spyOn(window.location, "replace").mockImplementation(() => {});
