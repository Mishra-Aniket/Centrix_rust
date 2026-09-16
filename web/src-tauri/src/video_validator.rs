use serde::Serialize;
use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use std::path::Path;

#[derive(Serialize)]
pub struct FileValidationReport {
    pub file_path: String,
    pub file_size_bytes: u64,
    pub file_format: String,
    pub is_valid: bool,
    pub is_locked: bool,
    pub status: String, // "Healthy", "Corrupted", "RecordingInProgress", "ZeroByte", "MissingMoov"
    pub error_message: Option<String>,
    pub recommendation: String,
}

#[tauri::command]
pub fn validate_recording_file(file_path: String) -> Result<FileValidationReport, String> {
    let path = Path::new(&file_path);

    if !path.exists() {
        return Ok(FileValidationReport {
            file_path: file_path.clone(),
            file_size_bytes: 0,
            file_format: "Unknown".into(),
            is_valid: false,
            is_locked: false,
            status: "NotFound".into(),
            error_message: Some("File does not exist on disk".into()),
            recommendation: "Check if the file was moved or deleted".into(),
        });
    }

    let meta = match std::fs::metadata(path) {
        Ok(m) => m,
        Err(e) => {
            return Ok(FileValidationReport {
                file_path: file_path.clone(),
                file_size_bytes: 0,
                file_format: "Unknown".into(),
                is_valid: false,
                is_locked: true,
                status: "Locked".into(),
                error_message: Some(format!("Cannot read file metadata: {}", e)),
                recommendation: "Check file permissions or if another program has an exclusive lock".into(),
            });
        }
    };

    let size = meta.len();

    // Zero-byte check
    if size == 0 {
        return Ok(FileValidationReport {
            file_path,
            file_size_bytes: 0,
            file_format: "Empty".into(),
            is_valid: false,
            is_locked: false,
            status: "ZeroByte".into(),
            error_message: Some("File size is 0 bytes (empty recording)".into()),
            recommendation: "Recording failed to write any data. Check camera and capture card connection.".into(),
        });
    }

    // Tiny file check (< 50 KB for video/PDF is almost always aborted immediately)
    if size < 50 * 1024 {
        return Ok(FileValidationReport {
            file_path,
            file_size_bytes: size,
            file_format: "Truncated".into(),
            is_valid: false,
            is_locked: false,
            status: "Truncated".into(),
            error_message: Some("File is less than 50 KB. Likely aborted after a few seconds.".into()),
            recommendation: "Discard this fragment and verify classroom recording schedule.".into(),
        });
    }

    // Attempt to open and read header
    let mut file = match File::open(path) {
        Ok(f) => f,
        Err(e) => {
            return Ok(FileValidationReport {
                file_path,
                file_size_bytes: size,
                file_format: "Unknown".into(),
                is_valid: false,
                is_locked: true,
                status: "RecordingInProgress".into(),
                error_message: Some(format!("File is currently locked: {}", e)),
                recommendation: "OBS or camera software is still writing to this file. Wait for recording to complete.".into(),
            });
        }
    };

    let ext = path.extension()
        .and_then(|e| e.to_str())
        .unwrap_or("")
        .to_lowercase();

    let mut header = [0u8; 64];
    let bytes_read = file.read(&mut header).unwrap_or(0);
    if bytes_read < 16 {
        return Ok(FileValidationReport {
            file_path,
            file_size_bytes: size,
            file_format: ext,
            is_valid: false,
            is_locked: false,
            status: "Corrupted".into(),
            error_message: Some("File header could not be read".into()),
            recommendation: "File is corrupted and cannot be used".into(),
        });
    }

    // --- Format-Specific Inspection ---

    // 1. MP4 / MOV / M4V inspection
    if ext == "mp4" || ext == "mov" || ext == "m4v" {
        // MP4 header check: bytes 4..8 should be "ftyp"
        let is_ftyp = &header[4..8] == b"ftyp";
        if !is_ftyp {
            return Ok(FileValidationReport {
                file_path,
                file_size_bytes: size,
                file_format: "MP4".into(),
                is_valid: false,
                is_locked: false,
                status: "Corrupted".into(),
                error_message: Some("Invalid MP4 header (missing 'ftyp' atom)".into()),
                recommendation: "File is not a valid MP4 container".into(),
            });
        }

        // Check for 'moov' atom presence by scanning boxes
        let has_moov = scan_for_atom(&mut file, size, b"moov");
        if !has_moov {
            return Ok(FileValidationReport {
                file_path,
                file_size_bytes: size,
                file_format: "MP4".into(),
                is_valid: false,
                is_locked: false,
                status: "MissingMoov".into(),
                error_message: Some("Corrupted MP4: missing 'moov' atom (recording was abruptly terminated without clean closing)".into()),
                recommendation: "Power outage or OBS crash occurred before file finalized. Run MP4 recovery tool before uploading.".into(),
            });
        }

        return Ok(FileValidationReport {
            file_path,
            file_size_bytes: size,
            file_format: "MP4".into(),
            is_valid: true,
            is_locked: false,
            status: "Healthy".into(),
            error_message: None,
            recommendation: "File container is intact and ready for upload".into(),
        });
    }

    // 2. MKV / WebM inspection
    if ext == "mkv" || ext == "webm" {
        // Matroska EBML signature: 0x1A 0x45 0xDF 0xA3
        let is_ebml = header.starts_with(&[0x1A, 0x45, 0xDF, 0xA3]);
        if !is_ebml {
            return Ok(FileValidationReport {
                file_path,
                file_size_bytes: size,
                file_format: "MKV".into(),
                is_valid: false,
                is_locked: false,
                status: "Corrupted".into(),
                error_message: Some("Invalid MKV header (missing EBML signature)".into()),
                recommendation: "File is not a valid Matroska/WebM video".into(),
            });
        }

        return Ok(FileValidationReport {
            file_path,
            file_size_bytes: size,
            file_format: "MKV".into(),
            is_valid: true,
            is_locked: false,
            status: "Healthy".into(),
            error_message: None,
            recommendation: "File container is intact and ready for upload".into(),
        });
    }

    // 3. PDF inspection
    if ext == "pdf" {
        let is_pdf = header.starts_with(b"%PDF-");
        if !is_pdf {
            return Ok(FileValidationReport {
                file_path,
                file_size_bytes: size,
                file_format: "PDF".into(),
                is_valid: false,
                is_locked: false,
                status: "Corrupted".into(),
                error_message: Some("Invalid PDF header (missing '%PDF-' signature)".into()),
                recommendation: "MaxHub export failed or file is corrupted".into(),
            });
        }

        // Check for %%EOF in the last 1024 bytes
        let has_eof = if size > 1024 {
            let _ = file.seek(SeekFrom::End(-1024));
            let mut tail = [0u8; 1024];
            let read_len = file.read(&mut tail).unwrap_or(0);
            tail[..read_len].windows(5).any(|w| w == b"%%EOF")
        } else {
            true
        };

        if !has_eof {
            return Ok(FileValidationReport {
                file_path,
                file_size_bytes: size,
                file_format: "PDF".into(),
                is_valid: false,
                is_locked: false,
                status: "Corrupted".into(),
                error_message: Some("PDF file is truncated (missing %%EOF marker)".into()),
                recommendation: "Digital board export was interrupted. Re-export notes from MaxHub.".into(),
            });
        }

        return Ok(FileValidationReport {
            file_path,
            file_size_bytes: size,
            file_format: "PDF".into(),
            is_valid: true,
            is_locked: false,
            status: "Healthy".into(),
            error_message: None,
            recommendation: "PDF notes file is intact".into(),
        });
    }

    // Generic fallback for other supported extensions (.mov, .avi, etc.)
    Ok(FileValidationReport {
        file_path,
        file_size_bytes: size,
        file_format: ext,
        is_valid: true,
        is_locked: false,
        status: "Healthy".into(),
        error_message: None,
        recommendation: "File is readable and passes basic integrity checks".into(),
    })
}

/// Fast scanner for MP4 top-level box signatures
fn scan_for_atom(file: &mut File, file_size: u64, target: &[u8; 4]) -> bool {
    let mut offset = 0u64;
    let mut buf = [0u8; 8];

    while offset + 8 <= file_size {
        if file.seek(SeekFrom::Start(offset)).is_err() {
            break;
        }
        if file.read_exact(&mut buf).is_err() {
            break;
        }

        let box_size = u32::from_be_bytes([buf[0], buf[1], buf[2], buf[3]]) as u64;
        let box_type = &buf[4..8];

        if box_type == target {
            return true;
        }

        if box_size == 0 {
            // Box extends to end of file
            break;
        } else if box_size == 1 {
            // Extended 64-bit size
            let mut ext_size_buf = [0u8; 8];
            if file.read_exact(&mut ext_size_buf).is_err() {
                break;
            }
            let ext_size = u64::from_be_bytes(ext_size_buf);
            if ext_size < 16 {
                break;
            }
            offset += ext_size;
        } else {
            if box_size < 8 {
                break; // Corrupted box size
            }
            offset += box_size;
        }
    }

    // Also check trailing 64KB (moov is frequently placed at the very end of MP4 files)
    if file_size > 65536 {
        let tail_offset = file_size - 65536;
        if file.seek(SeekFrom::Start(tail_offset)).is_ok() {
            let mut tail_buf = [0u8; 65536];
            if let Ok(read_len) = file.read(&mut tail_buf) {
                if tail_buf[..read_len].windows(4).any(|w| w == target) {
                    return true;
                }
            }
        }
    }

    false
}
