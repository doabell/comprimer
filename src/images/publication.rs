use super::{output_path, parent, publish_changes, stage};
use anyhow::{Result, bail, ensure};
use std::{
    fs,
    io::Read,
    path::{Path, PathBuf},
};

struct Entry {
    format: &'static str,
    source: PathBuf,
    destination: PathBuf,
    original: Option<tempfile::NamedTempFile>,
}

/// Own only outputs produced by this operation. Retain originals until verification
/// ends so changing the selected format can undo an earlier overwrite as well.
pub(super) struct Publication {
    overwrite: bool,
    entries: Vec<Entry>,
}

impl Publication {
    pub fn new(overwrite: bool) -> Self {
        Self {
            overwrite,
            entries: Vec::new(),
        }
    }

    pub fn paths(&self) -> Vec<PathBuf> {
        self.entries
            .iter()
            .map(|entry| entry.destination.clone())
            .collect()
    }

    pub fn matches(&self, candidates: &[(&'static str, PathBuf)]) -> bool {
        self.entries.len() == candidates.len()
            && candidates.iter().all(|(format, source)| {
                self.entries
                    .iter()
                    .any(|entry| entry.format == *format && entry.source == *source)
            })
    }

    pub fn is_empty(&self) -> bool {
        self.entries.is_empty()
    }

    pub fn set(
        &mut self,
        input: &Path,
        suffix: &str,
        candidates: &[(&'static str, PathBuf)],
    ) -> Result<()> {
        // Avoid deleting or replacing a provisional result edited by another program.
        for entry in &self.entries {
            ensure!(
                same_contents(&entry.source, &entry.destination)?,
                "Auto output changed during verification; leaving it untouched: {}",
                entry.destination.display()
            );
        }
        let mut next = Vec::new();
        let mut changes = Vec::new();
        for (format, source) in candidates {
            let previous = self.entries.iter().find(|entry| entry.format == *format);
            let destination = match previous {
                Some(entry) => entry.destination.clone(),
                None => output_path(input, format, suffix, self.overwrite)?,
            };
            let original = if previous.is_none() && destination.try_exists()? {
                ensure!(
                    self.overwrite,
                    "Output already exists: {}",
                    destination.display()
                );
                // Keep backups outside the work directory so recovery survives a
                // workspace cleanup if a later rollback is blocked by a file lock.
                Some(stage(&destination, parent(&destination))?)
            } else {
                None
            };
            if previous.is_none_or(|entry| entry.source != *source) {
                changes.push((
                    Some(source.clone()),
                    destination.clone(),
                    previous.is_some() || self.overwrite,
                ));
            }
            next.push(Entry {
                format,
                source: source.clone(),
                destination,
                original,
            });
        }
        for entry in &self.entries {
            if !candidates.iter().any(|(format, _)| *format == entry.format) {
                changes.push((
                    entry.original.as_ref().map(|file| file.path().to_owned()),
                    entry.destination.clone(),
                    true,
                ));
            }
        }
        publish_changes(&changes)?;
        for entry in &mut next {
            if let Some(previous) = self
                .entries
                .iter_mut()
                .find(|old| old.format == entry.format)
            {
                entry.original = previous.original.take();
            }
        }
        self.entries = next;
        Ok(())
    }

    pub fn rollback(&mut self) -> Result<()> {
        let mut changes = Vec::new();
        let mut edited = Vec::new();
        for entry in &self.entries {
            if same_contents(&entry.source, &entry.destination).unwrap_or(false) {
                changes.push((
                    entry.original.as_ref().map(|file| file.path().to_owned()),
                    entry.destination.clone(),
                    true,
                ));
            } else {
                edited.push(entry.destination.display().to_string());
            }
        }
        let result = publish_changes(&changes);
        if result.is_err() || !edited.is_empty() {
            let mut recovery = Vec::new();
            for entry in &mut self.entries {
                if let Some(backup) = entry.original.take() {
                    recovery.push(backup.keep().map(|(_, path)| path));
                }
            }
            bail!(
                "Auto rollback incomplete: {result:?}; externally changed outputs: {edited:?}; recovery files: {recovery:?}"
            );
        }
        self.entries.clear();
        Ok(())
    }
}

fn same_contents(a: &Path, b: &Path) -> Result<bool> {
    if !b.is_file() || fs::metadata(a)?.len() != fs::metadata(b)?.len() {
        return Ok(false);
    }
    let mut a = fs::File::open(a)?;
    let mut b = fs::File::open(b)?;
    let mut left = [0_u8; 64 * 1024];
    let mut right = [0_u8; 64 * 1024];
    loop {
        let count = a.read(&mut left)?;
        if count == 0 {
            return Ok(true);
        }
        b.read_exact(&mut right[..count])?;
        if left[..count] != right[..count] {
            return Ok(false);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::os::windows::fs::OpenOptionsExt;

    #[test]
    fn correction_reuses_names_and_restores_unselected_originals() {
        for overwrite in [false, true] {
            let dir = tempfile::tempdir().unwrap();
            let work = tempfile::tempdir().unwrap();
            let input = dir.path().join("image.png");
            let jpg = input.with_extension("jpg");
            fs::write(&input, b"original PNG").unwrap();
            fs::write(&jpg, b"original JPG").unwrap();
            let png_candidate = work.path().join("initial.png");
            let png_final = work.path().join("final.png");
            let jpg_candidate = work.path().join("candidate.jpg");
            fs::write(&png_candidate, b"provisional PNG").unwrap();
            fs::write(&png_final, b"optimized PNG").unwrap();
            fs::write(&jpg_candidate, b"encoded JPG").unwrap();
            let mut publication = Publication::new(overwrite);
            publication
                .set(&input, "", &[("png", png_candidate)])
                .unwrap();
            let first = publication.paths()[0].clone();
            publication.set(&input, "", &[("png", png_final)]).unwrap();
            assert_eq!(publication.paths(), vec![first.clone()]);
            assert_eq!(fs::read(&first).unwrap(), b"optimized PNG");
            publication
                .set(&input, "", &[("jpg", jpg_candidate)])
                .unwrap();
            assert_eq!(fs::read(&input).unwrap(), b"original PNG");
            if !overwrite {
                assert!(!first.exists());
            }
            assert_eq!(fs::read(&publication.paths()[0]).unwrap(), b"encoded JPG");
            publication.rollback().unwrap();
            assert_eq!(fs::read(&input).unwrap(), b"original PNG");
            assert_eq!(fs::read(&jpg).unwrap(), b"original JPG");
            assert_eq!(fs::read_dir(dir.path()).unwrap().count(), 2);
        }
    }

    #[test]
    fn failed_correction_rolls_back_to_the_visible_provisional_result() {
        let dir = tempfile::tempdir().unwrap();
        let work = tempfile::tempdir().unwrap();
        let input = dir.path().join("image.png");
        fs::write(&input, b"original").unwrap();
        let png = work.path().join("candidate.png");
        let jpg = work.path().join("candidate.jpg");
        fs::write(&png, b"provisional").unwrap();
        fs::write(&jpg, b"corrected").unwrap();
        let mut publication = Publication::new(false);
        publication.set(&input, "", &[("png", png)]).unwrap();
        let provisional = publication.paths()[0].clone();
        let locked = fs::OpenOptions::new()
            .read(true)
            .share_mode(1)
            .open(&provisional)
            .unwrap();
        assert!(publication.set(&input, "", &[("jpg", jpg)]).is_err());
        assert_eq!(fs::read(&provisional).unwrap(), b"provisional");
        assert!(!input.with_extension("jpg").exists());
        drop(locked);
        publication.rollback().unwrap();
        assert!(!provisional.exists());
        assert_eq!(fs::read(input).unwrap(), b"original");
    }

    #[test]
    fn correction_and_rollback_do_not_clobber_external_edits() {
        let dir = tempfile::tempdir().unwrap();
        let work = tempfile::tempdir().unwrap();
        let input = dir.path().join("image.png");
        fs::write(&input, b"original").unwrap();
        let png = work.path().join("candidate.png");
        let jpg = work.path().join("candidate.jpg");
        fs::write(&png, b"provisional").unwrap();
        fs::write(&jpg, b"corrected").unwrap();
        let mut publication = Publication::new(false);
        publication.set(&input, "", &[("png", png)]).unwrap();
        let provisional = publication.paths()[0].clone();
        fs::write(&provisional, b"external edit").unwrap();
        assert!(publication.set(&input, "", &[("jpg", jpg)]).is_err());
        assert!(publication.rollback().is_err());
        assert_eq!(fs::read(&provisional).unwrap(), b"external edit");
        assert!(!input.with_extension("jpg").exists());
        assert_eq!(fs::read(input).unwrap(), b"original");
    }
}
