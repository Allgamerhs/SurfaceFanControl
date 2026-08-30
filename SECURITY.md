# Security policy

## Supported releases

Only the latest GitHub release is supported. The portable and standard-setup monitoring editions
do not install or load a kernel driver or privileged service. Release hashes and VirusTotal report
links are published with every release.

## Kernel-driver status

The private engineering build historically used `WinRing0x64.sys`. Microsoft classifies WinRing0
as a known vulnerable driver (CVE-2020-14979). The binary is excluded from the public source tree,
and automated public release jobs must not publish a full-control installer while that dependency
is present. A clean antivirus result does not override this release gate.

## Reporting a vulnerability

Use GitHub's private vulnerability reporting feature in the repository's **Security** tab. Do not
include passwords, product keys, device serial numbers, SMBIOS UUIDs, private logs, or signing keys
in a public issue.

## Release verification

Compare the downloaded file against `SHA256SUMS.txt`, then open the matching VirusTotal report from
`VIRUSTOTAL.md`. The unsigned setup may receive heuristic or machine-learning detections because it
extracts application files and can create a startup entry. VirusTotal is supporting evidence, not a
guarantee that software is safe.
