# Rollback

1. Stop the Rewind Agent. Machine software must continue independently.
2. Preserve the configured data directory and completed incidents.
3. Replace the Agent artifact with the previously verified version.
4. Restore the previous immutable configuration.
5. Start the previous Agent and verify its pipe/health output.
6. Keep packages written by a newer schema read-only if the older Agent cannot
   interpret them; never rewrite completed evidence during rollback.

SDK rollback follows the host application's normal deployment process. Mixed
protocol-major versions fail closed at the Rewind boundary without stopping the
host application.
