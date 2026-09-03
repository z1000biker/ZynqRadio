# Security policy

Please report security-sensitive issues privately to the repository owner rather than publishing exploit details in a public issue.

The application opens a Hamlib-compatible TCP CAT listener. The default bind address is `127.0.0.1`; keeping it loopback-only is recommended unless remote CAT access is explicitly required and protected by the host firewall.
