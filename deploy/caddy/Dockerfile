# Caddy with the Cloudflare DNS plugin: the host is only reachable inside the tailnet, so
# Let's Encrypt can't use the HTTP challenge; the DNS-01 challenge works without any open port.
FROM caddy:2.10.2-builder-alpine AS builder
RUN xcaddy build --with github.com/caddy-dns/cloudflare@v0.2.4

FROM caddy:2.10.2-alpine
COPY --from=builder /usr/bin/caddy /usr/bin/caddy
COPY Caddyfile /etc/caddy/Caddyfile
