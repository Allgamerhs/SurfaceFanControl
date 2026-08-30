# Contributing

Contributions are welcome through GitHub pull requests.

1. Do not add firmware images, extracted firmware, device serial numbers, SMBIOS UUIDs, local IP
   addresses, credentials, signing material, or third-party driver binaries.
2. Keep hardware writes behind an exact model/controller fingerprint and an allowlist of registers.
3. Every write path must retain the verified firmware-AUTO rollback and watchdog behavior.
4. Run the core tests, the guard self-tests, and the portable package verification before opening a
   pull request.
5. Explain the tested device model and firmware without publishing a unique device identifier.
