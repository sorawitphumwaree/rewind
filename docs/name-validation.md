# Preliminary name validation

Checked: 2026-07-29.

Result: material conflict risk identified. On 2026-07-29 Ham explicitly chose to
retain “Rewind” because the intended NuGet IDs are available, accepting that
package availability is not trademark clearance.

Obvious conflicts found:

- Rewind Software Inc. operates a commercial software backup/recovery product at
  `rewind.com` and has a live registered US `REWIND` mark covering software/data
  backup, recovery, monitoring, and related services.
- GitHub already contains established software repositories named `rewind`,
  including Quarkslab's Windows kernel fuzzer.
- Other commercial and open-source products use Rewind for local rolling capture
  and retrieval concepts.

The proposed incident recorder is not identical to SaaS backup software, but the
shared software field, evidence-retention concept, exact word mark, occupied
domain, and crowded repository identity create avoidable confusion and legal risk.

The official NuGet API returned `404` for `Rewind.Abstractions`,
`Rewind.Protocol`, and `Rewind.Sdk` on 2026-07-29, meaning those exact IDs were
unregistered at that moment. They are not reserved until successfully published.

The same three official flat-container endpoints returned `404` again on
2026-08-02 during the pre-push artifact review. Availability can change until the
packages are first published.

Sources reviewed:

- https://rewind.com/
- https://tmsearch.uspto.gov/
- https://github.com/quarkslab/rewind

This is an engineering availability screen, not legal advice or trademark
clearance.
