/// The two output groups overlap within five percent of the smaller candidate.
/// Keep this comparison identical for preview predictions and final verification.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(super) enum Selection {
    Png,
    JpgWebp,
    Both,
}

impl Selection {
    pub fn from_sizes(png: u64, jpg: u64) -> Self {
        // Widen before multiplying: filesystem lengths can span all of u64.
        if u128::from(png.max(jpg)) * 100 <= u128::from(png.min(jpg)) * 105 {
            Self::Both
        } else if png < jpg {
            Self::Png
        } else {
            Self::JpgWebp
        }
    }

    pub fn formats(self) -> &'static [&'static str] {
        match self {
            Self::Png => &["png"],
            Self::JpgWebp => &["jpg", "webp"],
            Self::Both => &["png", "jpg", "webp"],
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn inclusive_margin_is_symmetric_and_safe_at_large_file_sizes() {
        for (png, jpg, expected) in [
            (100, 100, Selection::Both),
            (100, 105, Selection::Both),
            (105, 100, Selection::Both),
            (100, 106, Selection::Png),
            (106, 100, Selection::JpgWebp),
            (1, 2, Selection::Png),
            (u64::MAX, u64::MAX, Selection::Both),
            (u64::MAX / 2, u64::MAX, Selection::Png),
        ] {
            assert_eq!(Selection::from_sizes(png, jpg), expected);
        }
    }
}
