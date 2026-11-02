# FishyServer private HTTPS setup

Confirmed from user-provided output: Tailscale running; server name fishyserver.tail781f78.ts.net, Tailscale address 100.87.238.4. No SSH connection or deployment was performed by Codex. No SMTP relay has been selected/configured yet.

After installing and checking the included service, review existing Serve configuration first:

```sh
tailscale serve status
```

If it has no existing route that would conflict, publish the loopback service privately:

```sh
tailscale serve --bg http://127.0.0.1:8787
tailscale serve status
```

Follow the printed approval link if HTTPS needs enabling. This changes your Tailscale Serve configuration; run it only after the backend is installed. Do not use Funnel for this private deployment. Restrict access with your tailnet policy. All desktop clients must be connected to the appropriate Tailscale network. Use https://fishyserver.tail781f78.ts.net in FishTank. Never use the bare IP as the HTTPS origin: the certificate is issued for the DNS name.

Tailscale HTTPS certificates use public certificate transparency logs; the machine DNS name can appear there. Do not put secrets in that name. Serve provides transport, not email/SMS delivery. The backend still needs SMTP and an optional SMS gateway. A domain owned by you is normally needed for an authenticated transactional-email sender; the ts.net machine hostname is not a sender domain you control.

References: https://tailscale.com/docs/reference/tailscale-cli/serve and https://tailscale.com/docs/how-to/set-up-https-certificates
