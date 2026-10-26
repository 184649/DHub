# AIMANGA Camera Wall v2 — Static frontend only

- Main entry: `/manga/index.html`
- Six synthetic camera scenes with MP4 loops + WebP poster fallback
- One-camera, four-camera, automated camera tour, person focus, play/pause, fullscreen
- Uses the **existing authenticated read-only Notion API** at `/manga/api/v1/office`; API code, tokens, internal manuscripts and private logs are **not** stored here
- Footage is **not live surveillance, genuine employee recordings, or physical human motion footage**. The loops use synthetic photorealistic image stills with slow virtual camera movement. Worker presence comes exclusively from verified Notion records and may be unknown.
- CEO episode approval and Kindle publication remain in authorized Notion workflows, never automated by this static UI.

## Deployment caveat

GitHub Pages is a **public preview** and cannot authenticate against the private VPS endpoint. The production deployment requires the existing Basic-auth-protected Nginx route `/manga/`. Its CSP must explicitly allow `media-src 'self';` in addition to the previous image/script/style permissions. The VPS's static document root is `/srv/aimanga-office/current`. Keep `/etc/aimanga-office.env`, the Node read-only service, existing Dawn site, and Nginx routing for `/home` and `/api` unchanged. Production deployment instructions and rollback scripts are provided separately in the owner-only ZIP package.
