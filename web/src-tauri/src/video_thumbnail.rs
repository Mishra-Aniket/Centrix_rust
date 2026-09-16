use serde::Serialize;
use std::collections::hash_map::DefaultHasher;
use std::fs;
use std::hash::{Hash, Hasher};
use std::path::{Path, PathBuf};
use std::process::Command;

#[derive(Serialize)]
pub struct ThumbnailResult {
    pub success: bool,
    pub data_url: Option<String>,
    pub cached_path: Option<String>,
    pub width: u32,
    pub height: u32,
    pub source: String, // "ffmpeg", "cached", "html5_fallback"
}

/// Computes a short hash for a file path + modified time to invalidate cache on file changes
fn get_cache_key(file_path: &str) -> String {
    let mut hasher = DefaultHasher::new();
    file_path.hash(&mut hasher);
    if let Ok(meta) = fs::metadata(file_path) {
        if let Ok(mod_time) = meta.modified() {
            mod_time.hash(&mut hasher);
        }
        meta.len().hash(&mut hasher);
    }
    format!("{:x}", hasher.finish())
}

/// Thumbnail directory in temp storage
fn thumbnail_cache_dir() -> PathBuf {
    let dir = std::env::temp_dir().join("centrix_thumbnails");
    let _ = fs::create_dir_all(&dir);
    dir
}

/// Generates or retrieves a cached thumbnail for a video file
#[tauri::command]
pub fn get_video_thumbnail(file_path: String) -> Result<ThumbnailResult, String> {
    let path = Path::new(&file_path);
    if !path.exists() {
        return Err(format!("File does not exist: {}", file_path));
    }

    let cache_key = get_cache_key(&file_path);
    let cached_thumb = thumbnail_cache_dir().join(format!("{}.jpg", cache_key));

    // 1. Check if cache exists
    if cached_thumb.exists() {
        if let Ok(bytes) = fs::read(&cached_thumb) {
            let base64_str = base64_encode(&bytes);
            return Ok(ThumbnailResult {
                success: true,
                data_url: Some(format!("data:image/jpeg;base64,{}", base64_str)),
                cached_path: Some(cached_thumb.to_string_lossy().to_string()),
                width: 320,
                height: 180,
                source: "cached".to_string(),
            });
        }
    }

    // 2. Try FFmpeg extraction if available on system
    let ffmpeg_res = Command::new("ffmpeg")
        .args(&[
            "-ss", "00:00:03", // 3 seconds in to avoid pure black start
            "-i", &file_path,
            "-vframes", "1",
            "-q:v", "4",
            "-vf", "scale=320:-1",
            "-f", "image2",
            "-y",
            cached_thumb.to_str().unwrap_or(""),
        ])
        .output();

    if let Ok(output) = ffmpeg_res {
        if output.status.success() && cached_thumb.exists() {
            if let Ok(bytes) = fs::read(&cached_thumb) {
                let base64_str = base64_encode(&bytes);
                return Ok(ThumbnailResult {
                    success: true,
                    data_url: Some(format!("data:image/jpeg;base64,{}", base64_str)),
                    cached_path: Some(cached_thumb.to_string_lossy().to_string()),
                    width: 320,
                    height: 180,
                    source: "ffmpeg".to_string(),
                });
            }
        }
    }

    // 3. Fallback to HTML5 client-side extractor via Tauri asset protocol
    Ok(ThumbnailResult {
        success: false,
        data_url: None,
        cached_path: None,
        width: 320,
        height: 180,
        source: "html5_fallback".to_string(),
    })
}

/// Simple Base64 encoder without extra crate dependency
fn base64_encode(input: &[u8]) -> String {
    const CHARSET: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::with_capacity((input.len() + 2) / 3 * 4);

    for chunk in input.chunks(3) {
        let b0 = chunk[0];
        let b1 = if chunk.len() > 1 { chunk[1] } else { 0 };
        let b2 = if chunk.len() > 2 { chunk[2] } else { 0 };

        out.push(CHARSET[(b0 >> 2) as usize] as char);
        out.push(CHARSET[(((b0 & 3) << 4) | (b1 >> 4)) as usize] as char);

        if chunk.len() > 1 {
            out.push(CHARSET[(((b1 & 15) << 2) | (b2 >> 6)) as usize] as char);
        } else {
            out.push('=');
        }

        if chunk.len() > 2 {
            out.push(CHARSET[(b2 & 63) as usize] as char);
        } else {
            out.push('=');
        }
    }

    out
}
