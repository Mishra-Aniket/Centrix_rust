const sharp = require('sharp');

// The original Centrix logo:
// A circle divided into 4 quadrants by clean cross channels.
// In each quadrant, a swooping white feather/petal curves inward, creating the signature dynamic aperture.
const svg = `
<svg width="512" height="512" viewBox="0 0 512 512" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <linearGradient id="blueGrad" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#0066FF"/>
      <stop offset="100%" stop-color="#0047BA"/>
    </linearGradient>
  </defs>

  <!-- Top-Right Quadrant -->
  <g>
    <!-- Outer blue quadrant -->
    <path d="M 264 12 A 244 244 0 0 1 500 248 L 264 248 Z" fill="url(#blueGrad)" />
    <!-- White curved cutout leaf creating the inner petal -->
    <path d="M 264 116 C 340 144 424 248 424 248 C 344 228 276 182 264 116 Z" fill="#FFFFFF" />
  </g>

  <!-- Bottom-Right Quadrant -->
  <g>
    <path d="M 500 264 A 244 244 0 0 1 264 500 L 264 264 Z" fill="url(#blueGrad)" />
    <path d="M 396 264 C 368 340 264 424 264 424 C 284 344 330 276 396 264 Z" fill="#FFFFFF" />
  </g>

  <!-- Bottom-Left Quadrant -->
  <g>
    <path d="M 248 500 A 244 244 0 0 1 12 264 L 248 264 Z" fill="url(#blueGrad)" />
    <path d="M 248 396 C 172 368 88 264 88 264 C 168 284 236 330 248 396 Z" fill="#FFFFFF" />
  </g>

  <!-- Top-Left Quadrant -->
  <g>
    <path d="M 12 248 A 244 244 0 0 1 248 12 L 248 248 Z" fill="url(#blueGrad)" />
    <path d="M 116 248 C 144 172 248 88 248 88 C 228 168 182 236 116 248 Z" fill="#FFFFFF" />
  </g>
</svg>
`;

async function run() {
  const buf = Buffer.from(svg);
  await sharp(buf).resize(512, 512).png().toFile('/Users/aniketmishra/Desktop/Centrix/web/public/logo.png');
  await sharp(buf).resize(512, 512).png().toFile('/Users/aniketmishra/Desktop/Centrix/web/src/assets/logo.png');
  await sharp(buf).resize(512, 512).png().toFile('/Users/aniketmishra/Desktop/Centrix/agent/src/LectureAgent/wwwroot/logo.png');
  await sharp(buf).resize(512, 512).png().toFile('/Users/aniketmishra/LectureAgent/wwwroot/logo.png');
  await sharp(buf).resize(512, 512).png().toFile('/Users/aniketmishra/Desktop/Centrix/agent/src/LectureAgent.Desktop/Assets/app.png');
  await sharp(buf).resize(128, 128).png().toFile('/Users/aniketmishra/Desktop/Centrix/web/public/favicon.png');
  await sharp(buf).resize(128, 128).png().toFile('/Users/aniketmishra/Desktop/Centrix/agent/src/LectureAgent/wwwroot/favicon.png');

  // Tauri icons
  await sharp(buf).resize(512, 512).png().toFile('/Users/aniketmishra/Desktop/Centrix/web/src-tauri/icons/icon.png');
  await sharp(buf).resize(256, 256).png().toFile('/Users/aniketmishra/Desktop/Centrix/web/src-tauri/icons/128x128@2x.png');
  await sharp(buf).resize(128, 128).png().toFile('/Users/aniketmishra/Desktop/Centrix/web/src-tauri/icons/128x128.png');
  await sharp(buf).resize(32, 32).png().toFile('/Users/aniketmishra/Desktop/Centrix/web/src-tauri/icons/32x32.png');

  console.log('All Centrix logos and icons generated cleanly across web, agent, desktop, and tauri!');
}

run().catch(console.error);
