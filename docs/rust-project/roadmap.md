# Roadmap de Développement : Liturgical Calendar v2.0

**Version** : 2.0  
**Date de Révision** : 2026-02-19  
**Durée Totale Estimée** : 10 semaines  
**Méthodologie** : Développement incrémental orienté livrables binaires  
**Critères de Succès** : Tests de régression + Benchmarks + Fuzzing + Cross-build determinism

---

## Philosophie de la Roadmap v2.0

**Principe d'Organisation** : Chaque phase produit un **binaire utilisable** ou un **ensemble cohérent de crates testables**. Pas d'étapes intermédiaires sans validation concrète.

**Architecture Fondamentale** :

liturgical-calendar est un moteur déterministe AOT capable de produire un artefact annuel figé appelé **Kalendarium**, sérialisé au format `.kald` (magic `KALD`).

Le système est **complet et autonome** :
- Le **Slow Path** calcule n'importe quelle année grégorienne canonique (1583-4099)
- Le **Fast Path** (Kalendarium, fichier `.kald`) est une **optimisation spatiale et temporelle** pour une fenêtre choisie
- L'utilisateur décide de sa plage d'optimisation (typiquement -50/+300 ans autour de l'année courante)
- Le système continue de fonctionner pour toutes les autres années

**Le Kalendarium est un luxe de performance que l'on s'offre pour les années qui comptent vraiment.**

**Nouveautés v2.0** :
- Intégration du hardening production (validation header, corruption handling, observabilité)
- Tests de robustesse systématiques (fuzzing, cross-build)
- Outils de diagnostic et inspection
- FFI durci avec gestion d'erreurs
- CI/CD avec déterminisme cross-platform

---

## Phase 1 : Core + Diagnostics (Semaines 1-2)

### Objectif

Fondations robustes avec validation stricte et outils de diagnostic dès le départ.

### Livrable Principal

**Binaire** : `litu-core-test` — Suite de tests unitaires exécutable avec couverture ≥90%

### Tâches Détaillées

#### 1.1 Types Canoniques avec Corruption Handling (Semaine 1, Jours 1-3)

**Fichier** : `liturgical-calendar-core/src/types.rs`

**Structure Duale avec Gestion d'Erreur Riche** :

```rust
/// Représentation logique pour la Forge et le Slow Path
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Day {
    pub season: Season,
    pub color: Color,
    pub rank: Rank,
    pub feast_id: u32,
}

/// Représentation packed pour le Runtime (Fast Path)
#[repr(transparent)]
#[derive(Copy, Clone, Debug, PartialEq, Eq)]
pub struct DayPacked(u32);

impl DayPacked {
    /// Construction sécurisée avec validation des bits
    /// NOUVEAU : Retourne CorruptionInfo détaillé
    pub fn try_from_u32(packed: u32) -> Result<Self, CorruptionInfo> {
        Day::try_from_u32(packed)
            .map(|_| Self(packed))
            .map_err(|e| CorruptionInfo {
                packed_value: packed,
                invalid_field: e.field_name(),
                invalid_value: e.field_value(),
                offset: None,  // Sera rempli par le Provider
            })
    }

    /// SUPPRIMÉ : from_u32_or_invalid (comportement silencieux interdit)

    /// Extraction du u32 brut (zero-cost)
    #[inline(always)]
    pub fn as_u32(&self) -> u32 {
        self.0
    }

    /// Sentinelle pour entrées invalides
    ///
    /// INVARIANT : 0xFFFFFFFF — Season bits [31:28] = 15, hors domaine valide (max=6),
    /// rejeté par try_from_u8. Aucune entrée liturgique ne peut produire cette valeur.
    /// NE PAS utiliser 0x00000000 : décode en (TempusOrdinarium, Albus, Sollemnitas, id=0).
    pub fn invalid() -> Self {
        Self(0xFFFFFFFF)
    }

    /// Teste si ce DayPacked est la sentinelle d'erreur
    #[inline(always)]
    pub fn is_invalid(&self) -> bool {
        self.0 == 0xFFFFFFFF
    }
}

/// Information détaillée sur une corruption
#[derive(Debug, Clone)]
pub struct CorruptionInfo {
    pub packed_value: u32,
    pub invalid_field: &'static str,  // "season", "color", "rank"
    pub invalid_value: u8,
    pub offset: Option<usize>,  // Position dans le fichier
}
```

> **Hiérarchie d'erreurs** : la spécification §9.1 définit quatre types d'erreur distincts, un par couche du workspace (`DomainError`, `IoError`, `RegistryError`, `RuntimeError`). Toute implémentation Rust doit déclarer le type approprié dans le `src/error.rs` de chaque crate, et non un `Error` monolithique global.

**Tests Ajoutés** :

```rust
#[test]
fn test_corruption_detection() {
    // Season invalide (15 > 6)
    let packed = 0xF0000000;
    let result = DayPacked::try_from_u32(packed);
    assert!(result.is_err());
    
    let err = result.unwrap_err();
    assert_eq!(err.invalid_field, "season");
    assert_eq!(err.invalid_value, 15);
}

#[test]
fn test_all_valid_combinations() {
    use itertools::iproduct;
    
    for (s, c, r) in iproduct!(0..=6u8, 0..=5u8, 0..=5u8) {
        let logic = Day {
            season: Season::try_from_u8(s).unwrap(),
            color: Color::try_from_u8(c).unwrap(),
            rank: Rank::try_from_u8(r).unwrap(),
            feast_id: 0x123456,
        };
        
        let packed: u32 = logic.clone().into();
        let result = DayPacked::try_from_u32(packed);
        assert!(result.is_ok());
    }
}
```

**Critères de Validation** :

- ✅ `try_from_u32` rejette toutes les valeurs invalides avec info détaillée
- ✅ Zéro allocation dans le happy path
- ✅ `size_of::<DayPacked>() == 4`
- ✅ Tests unitaires : 100% passés

#### 1.2 Algorithme de Pâques + is_sunday (Semaine 1, Jours 4-5)

**Fichier** : `liturgical-calendar-core/src/easter.rs`

**Ajout is_sunday Déterministe** :

```rust
/// Détermine si un jour de l'année est un dimanche
/// Algorithme : Tomohiko Sakamoto (optimisé branchless)
///
/// GARANTIES :
/// - Aucune allocation
/// - Branchless dans le hot path
/// - Validé sur 1583-4099 (toutes les années grégoriennes)
///
/// Performance cible : <20ns
#[inline]
pub fn is_sunday(year: i32, day_of_year: u16) -> bool {
    let (month, day) = day_of_year_to_month_day(day_of_year, is_leap_year(year));
    day_of_week(year, month, day) == 0
}

/// Calcule le jour de la semaine (0=dimanche, 1=lundi, ..., 6=samedi)
/// Algorithme de Tomohiko Sakamoto
#[inline]
fn day_of_week(year: i32, month: u8, day: u8) -> u32 {
    const T: [u32; 12] = [0, 3, 2, 5, 0, 3, 5, 1, 4, 6, 2, 4];
    
    // Ajustement pour janvier et février
    let y = year - ((month < 3) as i32);
    let m = month as u32;
    let d = day as u32;
    
    ((y + y/4 - y/100 + y/400 + T[(m - 1) as usize] + d) as u32) % 7
}

/// Conversion DayOfYear → (Month, Day)
///
/// SIGNATURE CANONIQUE : (day_of_year: u16, is_leap: bool) — conforme spec §4.3.
/// L'appelant fournit is_leap précalculé (séparation des responsabilités).
///
/// Algorithme : soustraction itérative sur days_per_month.
/// Correct pour tous les mois, y compris décembre (doy > 334/335).
fn day_of_year_to_month_day(day_of_year: u16, is_leap: bool) -> (u8, u8) {
    let days_per_month: [u16; 12] = if is_leap {
        [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
    } else {
        [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
    };

    let mut remaining = day_of_year;
    for (month_idx, &days) in days_per_month.iter().enumerate() {
        if remaining <= days {
            return ((month_idx + 1) as u8, remaining as u8);
        }
        remaining -= days;
    }

    unreachable!()
}

#[inline]
pub fn is_leap_year(year: i32) -> bool {
    (year % 4 == 0) && (year % 100 != 0 || year % 400 == 0)
}
```

**Tests** :

```rust
#[test]
fn test_is_sunday_known_dates() {
    // Dates connues vérifiées contre calendrier officiel
    struct TestCase {
        year: i32,
        doy: u16,
        expected: bool,
        description: &'static str,
    }
    
    let cases = [
        TestCase { year: 2025, doy: 5, expected: true, description: "5 janv 2025 (dimanche)" },
        TestCase { year: 2025, doy: 110, expected: true, description: "20 avril 2025 (Pâques)" },
        TestCase { year: 2025, doy: 1, expected: false, description: "1 janv 2025 (mercredi)" },
        TestCase { year: 2024, doy: 366, expected: false, description: "31 déc 2024 (mardi)" },
        TestCase { year: 1583, doy: 293, expected: true, description: "20 oct 1583 (dimanche)" },
        TestCase { year: 4099, doy: 365, expected: false, description: "31 déc 4099 (jeudi)" },
        TestCase { year: 2000, doy: 1, expected: false, description: "1 janv 2000 (samedi)" },
        TestCase { year: 2000, doy: 2, expected: true, description: "2 janv 2000 (dimanche)" },
        TestCase { year: 1900, doy: 1, expected: false, description: "1 janv 1900 (lundi)" },
    ];
    
    for case in &cases {
        assert_eq!(
            is_sunday(case.year, case.doy),
            case.expected,
            "Failed for {}",
            case.description
        );
    }
}

#[test]
fn test_is_sunday_exhaustive_week() {
    // Vérification d'une semaine complète
    let year = 2025;
    let week_start = 5;  // 5 janv (dimanche)
    
    for offset in 0..7 {
        let is_expected_sunday = offset == 0;
        assert_eq!(
            is_sunday(year, week_start + offset),
            is_expected_sunday,
            "Failed for day {} (offset {})", week_start + offset, offset
        );
    }
}

#[test]
fn test_is_sunday_leap_year_boundary() {
    // Test autour du 29 février
    let year = 2024;  // Année bissextile
    
    // 60 = 29 février 2024 (jeudi)
    assert!(!is_sunday(year, 60));
    
    // 61 = 1 mars 2024 (vendredi)
    assert!(!is_sunday(year, 61));
    
    // 64 = 4 mars 2024 (lundi)
    assert!(!is_sunday(year, 64));
}

#[test]
fn test_day_of_year_to_month_day_roundtrip() {
    // Vérification que la conversion est correcte
    for year in [1583, 1900, 2000, 2024, 2025, 4099] {
        let max_day = if is_leap_year(year) { 366 } else { 365 };
        
        let is_leap = is_leap_year(year);
        for doy in 1..=max_day {
            let (month, day) = day_of_year_to_month_day(doy, is_leap);
            
            // Vérifications de base
            assert!(month >= 1 && month <= 12, "Invalid month {} for {}-{}", month, year, doy);
            assert!(day >= 1 && day <= 31, "Invalid day {} for {}-{}", day, year, doy);
            
            // Vérification cohérence avec is_leap_year
            if month == 2 {
                let max_feb = if is_leap_year(year) { 29 } else { 28 };
                assert!(day <= max_feb, "Invalid February day {} for year {}", day, year);
            }
        }
    }
}
```

**Benchmark** :

```rust
#[bench]
fn bench_is_sunday(b: &mut Bencher) {
    b.iter(|| {
        black_box(is_sunday(2025, 110))
    });
}
// Cible : < 20ns
```

#### 1.3 Header Validation avec Flags (Semaine 2, Jour 1)

**Fichier** : `liturgical-calendar-core/src/header.rs`

**Structure Header Étendue** :

```rust
/// Représentation logique du header (pas de layout mémoire direct)
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Header {
    pub magic: [u8; 4],      // "KALD"
    pub version: u16,        // 1
    pub start_year: i16,
    pub year_count: u16,
    pub flags: u16,          // Flags d'extension
    pub _padding: [u8; 4],
}

impl Header {
    /// Désérialise un header depuis 16 octets bruts (sans UB)
    pub fn from_bytes(bytes: &[u8]) -> Result<Self, HeaderError> {
        if bytes.len() < 16 {
            return Err(HeaderError::FileTooSmall);
        }
        
        let magic = [bytes[0], bytes[1], bytes[2], bytes[3]];
        let version = u16::from_ne_bytes([bytes[4], bytes[5]]);
        let start_year = i16::from_ne_bytes([bytes[6], bytes[7]]);
        let year_count = u16::from_ne_bytes([bytes[8], bytes[9]]);
        let flags = u16::from_ne_bytes([bytes[10], bytes[11]]);
        let padding = [bytes[12], bytes[13], bytes[14], bytes[15]];
        
        Ok(Header {
            magic,
            version,
            start_year,
            year_count,
            flags,
            _padding: padding,
        })
    }
}

/// Politique de Compatibilité Flags
///
/// PRINCIPE : Fail-fast sur flags inconnus pour v1.
/// Les versions futures (v2+) définiront KNOWN_FLAGS_V2, etc.
///
/// Justification :
/// - v1 est la version initiale, aucune extension n'est encore stabilisée
/// - Accepter des flags inconnus pourrait causer des corruptions silencieuses
/// - Les outils futurs (v2+) pourront migrer ou émettre des warnings
pub const KNOWN_FLAGS_V1: u16 = 0x0000;  // Aucun flag pour v1

/// Flags réservés pour v2+ (documentation anticipée)
///
/// Ces flags seront activés dans les versions futures :
/// - Bit 0 : Compression ZSTD (v2.1)
/// - Bit 1 : Checksums CRC32 (v2.1)
/// - Bit 2-3 : Rites (00=Ordinaire, 01=Extraordinaire) (v2.2)
/// - Bit 4-15 : Réservés
///
/// IMPORTANT : v1 rejette tous ces flags. v2+ les interprétera.

/// Validation stricte du header (sans UB, portable sur toutes architectures)
pub fn validate_header(bytes: &[u8]) -> Result<Header, HeaderError> {
    // Désérialisation explicite (pas de cast de pointeur → pas d'UB)
    let header = Header::from_bytes(bytes)?;
    
    // Magic
    if &header.magic != b"KALD" {
        return Err(HeaderError::InvalidMagic(header.magic));
    }
    
    // Version
    if header.version != 1 {
        return Err(HeaderError::UnsupportedVersion(header.version));
    }
    
    // Flags inconnus → REJET STRICT
    if (header.flags & !KNOWN_FLAGS_V1) != 0 {
        return Err(HeaderError::UnsupportedFlags {
            found: header.flags,
            known: KNOWN_FLAGS_V1,
            unknown_bits: header.flags & !KNOWN_FLAGS_V1,
        });
    }
    
    // Padding strict
    if header._padding != [0, 0, 0, 0] {
        return Err(HeaderError::InvalidPadding(header._padding));
    }
    
    // Bornes années
    if header.start_year < 1583 || header.start_year > 4099 {
        return Err(HeaderError::YearOutOfBounds(header.start_year));
    }
    
    if header.year_count == 0 || header.year_count > 2516 {
        return Err(HeaderError::InvalidYearCount(header.year_count));
    }
    
    Ok(header)
}

#[derive(Debug, Clone)]
pub enum HeaderError {
    FileTooSmall,
    InvalidMagic([u8; 4]),
    UnsupportedVersion(u16),
    UnsupportedFlags {
        found: u16,
        known: u16,
        unknown_bits: u16,
    },
    InvalidPadding([u8; 4]),
    YearOutOfBounds(i16),
    InvalidYearCount(u16),
}
```

**Tests** :

```rust
#[test]
fn test_validate_header_flags_unknown() {
    let mut bytes = vec![
        b'K', b'A', b'L', b'D',  // Magic
        0x01, 0x00,              // Version 1
        0xE9, 0x07,              // Start year 2025
        0x2C, 0x01,              // Year count 300
        0x01, 0x00,              // Flags 0x0001 (INCONNU pour v1)
        0x00, 0x00, 0x00, 0x00,  // Padding
    ];
    
    let result = validate_header(&bytes);
    assert!(matches!(result, Err(HeaderError::UnsupportedFlags { .. })));
}

#[test]
fn test_validate_header_padding_non_zero() {
    let mut bytes = vec![
        b'K', b'A', b'L', b'D',
        0x01, 0x00,
        0xE9, 0x07,
        0x2C, 0x01,
        0x00, 0x00,
        0xFF, 0x00, 0x00, 0x00,  // Padding invalide
    ];
    
    let result = validate_header(&bytes);
    assert!(matches!(result, Err(HeaderError::InvalidPadding(_))));
}
```

#### 1.4 Outil d'Inspection (Semaine 2, Jours 2-3)

**Binaire** : `kald-inspect` — Utilitaire CLI de diagnostic

**Fichier** : `kald-inspect/src/main.rs`

```rust
use clap::Parser;
use liturgical_calendar_core::{Header, Day, CorruptionInfo};

#[derive(Parser)]
#[command(name = "kald-inspect")]
#[command(about = "Diagnostic tool for .kald files")]
struct Args {
    /// Path to .kald file
    file: String,
    
    /// Show first N entries
    #[arg(short, long, default_value = "10")]
    preview: usize,
    
    /// Check for corruptions
    #[arg(short, long)]
    check: bool,
}

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let args = Args::parse();
    
    let file = std::fs::File::open(&args.file)?;
    let mmap = unsafe { memmap2::Mmap::map(&file)? };
    
    // Validation header
    let header = validate_header(&mmap[..])?;
    
    println!("Format: KALD v{}", header.version);
    println!("Start Year: {}", header.start_year);
    println!("Year Count: {}", header.year_count);
    println!("Flags: 0x{:04X}", header.flags);
    println!("File Size: {} bytes", mmap.len());
    
    let expected_size = 16 + (header.year_count as usize * 1464);
    println!("Expected Size: {} bytes {}", 
        expected_size,
        if mmap.len() == expected_size { "✓" } else { "✗" }
    );
    
    // Diagnostic endianness
    diagnose_endianness(&header);
    
    // Preview entries
    if args.preview > 0 {
        preview_entries(&mmap, &header, args.preview)?;
    }
    
    // Corruption scan
    if args.check {
        scan_corruptions(&mmap, &header)?;
    }
    
    Ok(())
}

fn diagnose_endianness(header: &Header) {
    let system_endian = if cfg!(target_endian = "little") {
        "little-endian"
    } else {
        "big-endian"
    };
    
    println!("System Endianness: {}", system_endian);
    
    // Heuristique : start_year doit être plausible
    if header.start_year < 1583 || header.start_year > 4099 {
        println!("⚠️  WARNING: start_year looks implausible, possible endianness mismatch");
    } else {
        println!("✓ Endianness check passed");
    }
}

fn preview_entries(
    mmap: &memmap2::Mmap,
    header: &Header,
    count: usize
) -> Result<(), Box<dyn std::error::Error>> {
    println!("\nFirst {} entries:", count);
    
    let data = &mmap[16..];
    for i in 0..count.min(header.year_count as usize * 366) {
        let offset = i * 4;
        let packed = u32::from_ne_bytes([
            data[offset],
            data[offset + 1],
            data[offset + 2],
            data[offset + 3],
        ]);
        
        let year = header.start_year + (i / 366) as i16;
        let day = (i % 366) + 1;
        
        match Day::try_from_u32(packed) {
            Ok(logic) => {
                println!("  {}-{:03}: 0x{:08X} ({:?}, {:?}, {:?}, #0x{:06X})",
                    year, day, packed,
                    logic.season, logic.color, logic.rank, logic.feast_id
                );
            }
            Err(_) => {
                println!("  {}-{:03}: 0x{:08X} ⚠️  CORRUPTED", year, day, packed);
            }
        }
    }
    
    Ok(())
}

fn scan_corruptions(
    mmap: &memmap2::Mmap,
    header: &Header
) -> Result<(), Box<dyn std::error::Error>> {
    println!("\nScanning for corruptions...");
    
    let data = &mmap[16..];
    let total = header.year_count as usize * 366;
    let mut corrupted = 0;
    
    for i in 0..total {
        let offset = i * 4;
        let packed = u32::from_ne_bytes([
            data[offset],
            data[offset + 1],
            data[offset + 2],
            data[offset + 3],
        ]);
        
        if let Err(info) = DayPacked::try_from_u32(packed) {
            corrupted += 1;
            eprintln!("Corruption at offset {}: 0x{:08X} (field: {}, value: {})",
                offset, info.packed_value, info.invalid_field, info.invalid_value
            );
        }
    }
    
    println!("\nCorruption Report:");
    println!("  Total entries: {}", total);
    println!("  Corrupted: {}", corrupted);
    println!("  Rate: {:.4}%", (corrupted as f64 / total as f64) * 100.0);
    
    if corrupted == 0 {
        println!("✓ No corruptions detected");
    } else {
        println!("✗ File contains corrupted entries");
    }
    
    Ok(())
}
```

**Tests d'Intégration** :

```bash
# Test sur fichier valide
$ kald-inspect france.kald --preview 5 --check
# Attendu : 0 corruptions

# Test sur fichier corrompu
$ kald-inspect corrupted.kald --check
# Attendu : corruption détectée et rapportée
```

#### 1.5 Metrics de Phase 1

**Critères de Validation** :

- ✅ `litu-core-test` : 100% tests passés
- ✅ `kald-inspect` : compile et détecte corruptions
- ✅ Coverage : ≥90% (cargo-tarpaulin)
- ✅ Zero clippy warnings
- ✅ `is_sunday` : <20ns/appel (benchmark)
- ✅ Header validation : rejette tous les cas invalides

**Livrables Concrets** :

- `liturgical-calendar-core-0.1.0` (crate)
- `kald-inspect-0.1.0` (binaire)
- Suite de tests avec 90%+ coverage

---

## Phase 2 : Forge Déterministe + Registry (Semaines 3-5)

### Objectif

Forge production-ready générant des fichiers `.kald` pour une **fenêtre temporelle choisie par l'utilisateur**. La Forge ne "compile" pas tout le calendrier : elle optimise une plage stratégique (ex: 2025-2324 pour serveur, 2000-2100 pour mobile).

**Choix de Fenêtre - Exemples** :
- **Application mobile contemporaine** : 2000-2100 (100 ans, ~150 KB)
- **Serveur avec fenêtre glissante** : année_courante ±50 ans (régénéré annuellement)
- **Archive historique** : 1583-2025 (442 ans, ~650 KB)
- **Calendrier perpétuel moderne** : 1900-2200 (300 ans, ~440 KB)

Hors fenêtre : le runtime bascule automatiquement sur le Slow Path (transparent pour l'utilisateur, latence <10µs au lieu de <100ns).

### Livrable Principal

**Binaire** : `liturgical-calendar-forge` — Générateur de calendriers avec registry canonique

### Tâches Détaillées

#### 2.1 FeastID Registry avec Import/Export (Semaine 3, Jours 1-3)

**Fichier** : `liturgical-calendar-forge/src/registry.rs`

**Thread-Safe Allocator avec Mesure de Contention** :

```rust
use std::collections::BTreeMap;
use serde::{Serialize, Deserialize};

/// Registry canonique conforme spec §3.2.
///
/// MODÈLE D'ACCÈS : &mut self — mono-thread à la Forge.
/// La Forge génère le .kald en séquentiel ; aucune concurrence sur le registry.
/// Si un usage multi-thread s'avère nécessaire (v2.x), encapsuler dans Arc<Mutex<FeastRegistry>>.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FeastRegistry {
    /// Mappage FeastID → Nom canonique
    pub allocations: BTreeMap<u32, String>,

    /// Compteurs par scope/category pour allocation séquentielle
    pub next_id: BTreeMap<(u8, u8), u16>,
}

impl FeastRegistry {
    /// Alloue le prochain ID disponible pour un scope/category.
    ///
    /// ERREUR : RegistryError (couche Forge) — conforme spec §9.1.
    pub fn allocate_next(&mut self, scope: u8, category: u8) -> Result<u32, RegistryError> {
        if scope > 3 || category > 15 {
            return Err(RegistryError::InvalidScopeCategory { scope, category });
        }

        let key = (scope, category);
        let current = self.next_id.entry(key).or_insert(0);

        if *current == 0xFFFF {
            return Err(RegistryError::FeastIDExhausted { scope, category });
        }

        let feast_id = ((scope as u32) << 20)
            | ((category as u32) << 16)
            | (*current as u32);

        *current += 1;

        Ok(feast_id)
    }

    /// Enregistre une allocation avec nom canonique.
    pub fn register(&mut self, feast_id: u32, name: String) -> Result<(), RegistryError> {
        if self.allocations.contains_key(&feast_id) {
            return Err(RegistryError::FeastIDCollision(feast_id));
        }
        self.allocations.insert(feast_id, name);
        Ok(())
    }

    /// Export d'un scope/category pour partage entre forges.
    pub fn export_scope(&self, scope: u8, category: u8) -> RegistryExport {
        let prefix = ((scope as u32) << 20) | ((category as u32) << 16);
        let mask = 0x3F0000u32;

        let allocations: Vec<(u32, String)> = self.allocations
            .iter()
            .filter(|(id, _)| (**id & mask) == prefix)
            .map(|(id, name)| (*id, name.clone()))
            .collect();

        RegistryExport {
            scope,
            category,
            version: 1,
            allocations,
        }
    }

    /// Import avec détection de collisions.
    ///
    /// COMPORTEMENT (conforme spec §3.3) :
    /// - Retourne Ok(report) même en présence de collisions.
    /// - Les collisions sont dans ImportReport::collisions — non fatales.
    /// - Err uniquement pour erreurs structurelles (I/O, intégrité).
    pub fn import(&mut self, export: RegistryExport) -> Result<ImportReport, RegistryError> {
        let mut report = ImportReport {
            imported: 0,
            skipped: 0,
            collisions: Vec::new(),
        };

        for (feast_id, name) in export.allocations {
            if let Some(existing) = self.allocations.get(&feast_id) {
                if existing != &name {
                    report.collisions.push(CollisionInfo {
                        feast_id,
                        existing: existing.clone(),
                        incoming: name.clone(),
                    });
                    report.skipped += 1;
                } else {
                    report.skipped += 1;  // Déjà présent, identique — pas d'erreur
                }
            } else {
                self.allocations.insert(feast_id, name);
                report.imported += 1;
            }
        }

        // Mise à jour du compteur next_id
        let key = (export.scope, export.category);
        let max_seq = self.allocations
            .keys()
            .filter(|id| (**id & 0x3F0000u32) == ((export.scope as u32) << 20 | (export.category as u32) << 16))
            .map(|id| (id & 0xFFFF) as u16)
            .max()
            .unwrap_or(0);

        self.next_id.insert(key, max_seq + 1);

        Ok(report)  // Collisions dans le rapport, non fatales
    }
}

#[derive(Debug, Clone)]
pub struct ImportReport {
    pub imported: usize,
    pub skipped: usize,
    pub collisions: Vec<CollisionInfo>,
}

#[derive(Debug, Clone)]
pub struct CollisionInfo {
    pub feast_id: u32,
    pub existing: String,
    pub incoming: String,
}
```

> **Extension v2.x (multi-thread)** : si la Forge évolue vers un pipeline concurrent, encapsuler dans `Arc<Mutex<FeastRegistry>>` ou implémenter un `ShardedRegistry` (sharding par scope/category). À ne pas anticiper avant qu'un benchmark de contention le justifie.

**Benchmark Registry** :

```rust
#[bench]
fn bench_registry_allocate_next(b: &mut Bencher) {
    let mut registry = FeastRegistry::new();

    b.iter(|| {
        // La Forge alloue séquentiellement — pas de contention
        registry.allocate_next(2, 1).unwrap()
    });
}
```

> **Note** : Aucun benchmark parallèle — le registry est mono-thread à la Forge (modèle `&mut self`). Si un usage concurrent s'avère nécessaire en v2.x, encapsuler dans `Arc<Mutex<FeastRegistry>>` et réévaluer avec un benchmark rayon.

**Décision d'Implémentation** :

Pour v1.0, le registry simple (`&mut self`, mono-thread) est suffisant :
- La Forge génère le `.kald` séquentiellement
- Pas d'accès concurrent au registry durant la compilation
- Le modèle `BTreeMap` garantit le déterminisme de l'output



**CLI Registry Commands** :

```bash
# Export d'un scope
$ liturgical-calendar-forge registry export \
    --scope 2 --category 1 \
    --output france_sanctoral.json

# Import
$ liturgical-calendar-forge registry import \
    --file france_sanctoral.json \
    --registry germany.json

# Vérification
$ liturgical-calendar-forge registry verify \
    --registry france.json
```

**Tests** :

```rust
#[test]
fn test_registry_import_export() {
    let mut registry_fr = FeastRegistry::new();
    
    // Allocation France
    for i in 0..100 {
        let id = registry_fr.allocate_next(2, 1).unwrap();
        registry_fr.register(id, format!("Saint FR {}", i)).unwrap();
    }
    
    // Export
    let export = registry_fr.export_scope(2, 1);
    assert_eq!(export.allocations.len(), 100);
    
    // Import dans registry allemand
    let mut registry_de = FeastRegistry::new();
    let report = registry_de.import(export).unwrap();
    
    assert_eq!(report.imported, 100);
    assert_eq!(report.collisions.len(), 0);
    
    // Allocation allemande ne doit pas collisionner
    let de_id = registry_de.allocate_next(2, 1).unwrap();
    assert!(de_id > 0x210063);  // Au-delà des 100 français
}

#[test]
fn test_registry_collision_detection() {
    let mut registry = FeastRegistry::new();
    
    // Allocation manuelle
    registry.register(0x210001, "Saint A".to_string()).unwrap();
    
    // Export conflit
    let mut export = RegistryExport {
        scope: 2,
        category: 1,
        version: 1,
        allocations: vec![
            (0x210001, "Saint B".to_string()),  // Collision !
        ],
    };

    // Conforme spec §3.3 : import retourne Ok même en présence de collisions.
    // Les collisions sont dans le rapport, non fatales.
    let result = registry.import(export);
    assert!(result.is_ok());
    let report = result.unwrap();
    assert_eq!(report.collisions.len(), 1);
    assert_eq!(report.collisions[0].feast_id, 0x210001);
    assert_eq!(report.collisions[0].existing, "Saint A");
    assert_eq!(report.collisions[0].incoming, "Saint B");
    assert_eq!(report.skipped, 1);
}
```

#### 2.2 CalendarBuilder avec BTreeMap Strict (Semaine 3-4)

**Fichier** : `liturgical-calendar-forge/src/builder.rs`

**Note Configuration Liturgique** :

La roadmap v2.0 se concentre sur l'architecture et les contrats du système. **Le contenu liturgique exhaustif** (règles temporelles, sanctoral, fêtes votives, déplacements) sera fourni par l'opérateur via fichiers de configuration (TOML/JSON).

La spécification technique (section 5.1) détaille le format attendu pour ces configurations. Pour v1.0, l'opérateur doit fournir :
- Règles temporelles (fêtes mobiles, déplacements)
- Sanctoral complet (universel, national, diocésain)
- Règles de précédence

**Déterminisme Garanti** :

```rust
use std::collections::BTreeMap;

pub struct CalendarBuilder {
    config: Config,
    registry: FeastRegistry,
    slow_path: SlowPath,

    /// CRITIQUE : BTreeMap pour déterminisme (ordre de sérialisation garanti).
    /// Type : DayPacked — cohérent avec le Data Body du .kald (u32 par entrée).
    /// La Forge calcule via Day (SlowPath) puis convertit immédiatement en DayPacked.
    cache: BTreeMap<(i16, u16), DayPacked>,
}

impl CalendarBuilder {
    /// Construit le Kalendarium pour la fenêtre configurée.
    ///
    /// ERREUR : RuntimeError (couche runtime/forge) — conforme spec §9.1.
    /// La variant utilisée est RuntimeError::Domain(DomainError::YearOutOfBounds)
    /// pour les bornes hors domaine grégorien canonique.
    pub fn build(mut self) -> Result<Calendar, RuntimeError> {
        let start = self.config.start_year;
        let end = start + self.config.year_count as i16;

        // Validation bornes stricte
        if start < 1583 || end > 4099 {
            return Err(RuntimeError::Domain(DomainError::YearOutOfBounds(start)));
        }

        // Génération déterministe (ordre des années garanti par BTreeMap)
        for year in start..end {
            let max_day = if is_leap_year(year as i32) { 366 } else { 365 };

            for day in 1..=max_day {
                // SlowPath produit Day (logique) → converti immédiatement en DayPacked
                let liturgical_day: DayPacked = self.slow_path.compute(year, day)
                    .map(DayPacked::from)
                    .map_err(RuntimeError::Domain)?;
                self.cache.insert((year, day), liturgical_day);
            }

            // Padding jour 366 pour années non-bissextiles
            // Sentinelle 0xFFFFFFFF — hors domaine valide, détectable à l'inspection
            if max_day == 365 {
                self.cache.insert((year, 366), DayPacked::invalid());
            }
        }

        Ok(Calendar {
            start_year: start,
            year_count: self.config.year_count,
            data: self.cache,
        })
    }
}
```

**Test de Déterminisme** :

```rust
#[test]
fn test_forge_determinism_multiple_runs() {
    let config = Config::load("test.toml").unwrap();
    
    let mut hashes = Vec::new();
    
    for run in 0..5 {
        let builder = CalendarBuilder::new(config.clone()).unwrap();
        let calendar = builder.build().unwrap();
        
        let mut file = Vec::new();
        calendar.write_kald(&mut file).unwrap();
        
        let hash = sha256::digest(&file);
        hashes.push(hash);
    }
    
    // Tous les hashes doivent être identiques
    assert!(hashes.windows(2).all(|w| w[0] == w[1]));
}
```

#### 2.3 Binaire liturgical-calendar-forge Complet (Semaine 5)

**CLI Commands** :

```bash
# Build standard
$ liturgical-calendar-forge build \
    --config france.toml \
    --output france.kald \
    --lang-output france.lits

# Build avec vérification
$ liturgical-calendar-forge build \
    --config france.toml \
    --verify \
    --output france.kald

# Registry management
$ liturgical-calendar-forge registry export --scope 2 --category 1 -o export.json
$ liturgical-calendar-forge registry import --file export.json
$ liturgical-calendar-forge registry verify
```

**Metrics Phase 2** :

- ✅ `liturgical-calendar-forge` : build france.kald (300 ans) en <10s
- ✅ Déterminisme : SHA-256 identique sur 5 runs
- ✅ Registry : import/export sans collisions
- ✅ Tests : 100% passés

**Livrables** :

- `liturgical-calendar-forge-0.1.0` (binaire)
- `france.kald` (fichier de test référence)

---

## Phase 3 : Runtime Robuste + Observabilité (Semaines 6-7)

### Objectif

Runtime production-grade avec **deux chemins de calcul fonctionnellement équivalents** :

1. **Fast Path** : Lecture mmap du `.kald` pour la fenêtre optimisée (<100ns)
2. **Slow Path** : Calcul algorithmique pour toutes les années 1583-4099 (<10µs)

La sélection Fast/Slow est une **optimisation de performance**, pas une correction d'erreur. Les deux chemins produisent des résultats identiques (validé par tests d'identité).

**Mode Slow-Only** : Le runtime peut fonctionner sans fichier `.kald` (utile pour recherche historique ou contraintes mémoire).

### Avertissements Compilateur Anticipés (Phase 3 spécifique)

Trois comportements du compilateur Rust sont prévisibles dans cette phase.
Les connaître avant l'implémentation évite des sessions de débogage longues.

#### A. Self-referential struct — `FastPath` et le mmap

Le compilateur **refusera** toute tentative de stocker une référence dérivée
du `Mmap` directement dans la même struct :

```rust
// ❌ RUSTC ERROR : cannot infer an appropriate lifetime
struct FastPath {
    mmap: Mmap,
    data: &[u32],  // Rust ne peut pas annoter ce lifetime
}
```

**Solution retenue** (spec §7.2) : `parse_data_body` retourne `&'static [u32]`
via `unsafe`, avec l'invariant que `FastPath` possède le `Mmap`.
Ne pas tenter d'annoter un lifetime non-`'static` sur ce champ — cela cascade
sur `Provider`, puis sur toutes les fonctions publiques.

#### B. Conversions `From` manquantes — opérateur `?` cross-crate

L'opérateur `?` propage les erreurs via `From::from()`. Si une conversion
`From<X> for Y` est absente, le compilateur signale :

```
the trait `From<IoError>` is not implemented for `RuntimeError`
```

Cette erreur apparaît au *site d'appel* (là où `?` est utilisé), pas au niveau
de la définition du type — ce qui peut être déroutant. Implémenter toutes les
conversions du tableau §9.1 de la spec *avant* d'écrire le code qui les utilise.

Ordre d'implémentation : `std::io::Error → IoError` en premier, puis
`IoError → RuntimeError`, puis `DomainError → RuntimeError`.

#### C. Blocs `unsafe` FFI — préconditions non vérifiables

Le compilateur Rust ne vérifie **aucune précondition** à l'intérieur d'un bloc
`unsafe`. Il fait confiance au commentaire `// SAFETY :` que vous rédigez.
Toute violation de ces préconditions côté C est un comportement indéfini (UB)
silencieux — pas une erreur compilateur, pas une erreur runtime Rust.

Les sections `/// # Safety` de chaque fonction FFI (spec §10.2) constituent le
contrat formel. Les lire avant d'écrire les tests C de la tâche 3.2.

### Livrable Principal

**Bibliothèque** : `liturgical-calendar-runtime` (Rust + C bindings)

### Tâches Détaillées

#### 3.1 Provider avec Télémétrie (Semaine 6, Jours 1-3)

**Fichier** : `liturgical-calendar-runtime/src/provider.rs`

**Instrumentation Complète** :

```rust
use std::sync::atomic::{AtomicU64, Ordering};

pub struct Provider {
    fast_path: Option<FastPath>,
    slow_path: SlowPath,
    range: (i16, u16),
    string_provider: StringProvider,
    
    /// NOUVEAU : Télémétrie atomique
    telemetry: Telemetry,
}

#[derive(Default)]
pub struct Telemetry {
    fast_path_hits: AtomicU64,
    slow_path_hits: AtomicU64,
    invalid_returns: AtomicU64,
    corrupted_entries: AtomicU64,
    out_of_bounds_queries: AtomicU64,
}

impl Provider {
    pub fn get_day(&self, year: i16, day_of_year: u16) -> DayPacked {
        // Validation stricte
        if day_of_year == 0 || day_of_year > 366 {
            self.telemetry.invalid_returns.fetch_add(1, Ordering::Relaxed);
            return DayPacked::invalid();
        }
        
        // Validation année bissextile
        if day_of_year == 366 && !is_leap_year(year as i32) {
            self.telemetry.invalid_returns.fetch_add(1, Ordering::Relaxed);
            return DayPacked::invalid();
        }
        
        // Tentative Fast Path
        if let Some(ref fast) = self.fast_path {
            if year >= self.range.0 && year < self.range.0 + self.range.1 as i16 {
                self.telemetry.fast_path_hits.fetch_add(1, Ordering::Relaxed);
                
                let idx = index_day(year, day_of_year, fast.start_year);
                let packed = fast.data[idx];
                
                match DayPacked::try_from_u32(packed) {
                    Ok(day) => return day,
                    Err(_) => {
                        // Corruption détectée → log structuré
                        self.telemetry.corrupted_entries.fetch_add(1, Ordering::Relaxed);
                        self.log_corruption(year, day_of_year, packed);
                        return DayPacked::invalid();
                    }
                }
            }
        }
        
        // Fallback Slow Path
        if year >= 1583 && year <= 4099 {
            self.telemetry.slow_path_hits.fetch_add(1, Ordering::Relaxed);
            return self.slow_path.compute(year, day_of_year)
                .map(|logic| DayPacked::from(logic))
                .unwrap_or_else(|| DayPacked::invalid());
        }
        
        // Hors limites
        self.telemetry.out_of_bounds_queries.fetch_add(1, Ordering::Relaxed);
        DayPacked::invalid()
    }
    
    /// Log structuré de corruption (JSON vers stderr)
    ///
    /// SIGNATURE CANONIQUE (conforme spec §7) : reçoit le u32 brut.
    /// Le CorruptionInfo est reconstruit ici — pas en amont — pour rester
    /// cohérent avec le point d'appel où seul le packed est disponible.
    fn log_corruption(&self, year: i16, day_of_year: u16, packed: u32) {
        let (invalid_field, invalid_value) = match DayPacked::try_from_u32(packed) {
            Ok(_) => ("none", 0u8),  // Ne devrait pas arriver ici
            Err(info) => (info.invalid_field, info.invalid_value),
        };

        let log = serde_json::json!({
            "timestamp": chrono::Utc::now().to_rfc3339(),
            "event": "corruption_detected",
            "year": year,
            "day_of_year": day_of_year,
            "packed_value": format!("0x{:08X}", packed),
            "invalid_field": invalid_field,
            "invalid_value": invalid_value,
            "offset": index_day(year, day_of_year, self.range.0),
        });

        eprintln!("{}", log);
    }
    
    /// API de télémétrie (snapshot atomique)
    pub fn get_telemetry(&self) -> TelemetrySnapshot {
        TelemetrySnapshot {
            fast_path_hits: self.telemetry.fast_path_hits.load(Ordering::Relaxed),
            slow_path_hits: self.telemetry.slow_path_hits.load(Ordering::Relaxed),
            invalid_returns: self.telemetry.invalid_returns.load(Ordering::Relaxed),
            corrupted_entries: self.telemetry.corrupted_entries.load(Ordering::Relaxed),
            out_of_bounds_queries: self.telemetry.out_of_bounds_queries.load(Ordering::Relaxed),
        }
    }
}

#[derive(Debug, Clone, Copy)]
pub struct TelemetrySnapshot {
    pub fast_path_hits: u64,
    pub slow_path_hits: u64,
    pub invalid_returns: u64,
    pub corrupted_entries: u64,
    pub out_of_bounds_queries: u64,
}

impl TelemetrySnapshot {
    pub fn hit_rate(&self) -> f64 {
        let total = self.fast_path_hits + self.slow_path_hits;
        if total == 0 { 0.0 } else { self.fast_path_hits as f64 / total as f64 }
    }
    
    /// Export au format Prometheus (text exposition)
    pub fn to_prometheus(&self) -> String {
        format!(
            "# HELP kal_fast_path_hits Total fast path queries\n\
             # TYPE kal_fast_path_hits counter\n\
             kal_fast_path_hits {}\n\
             # HELP kal_slow_path_hits Total slow path queries\n\
             # TYPE kal_slow_path_hits counter\n\
             kal_slow_path_hits {}\n\
             # HELP kal_invalid_returns Invalid day_of_year requests\n\
             # TYPE kal_invalid_returns counter\n\
             kal_invalid_returns {}\n\
             # HELP kal_corrupted_entries Corrupted entries detected\n\
             # TYPE kal_corrupted_entries counter\n\
             kal_corrupted_entries {}\n\
             # HELP kal_out_of_bounds_queries Out-of-bounds year requests\n\
             # TYPE kal_out_of_bounds_queries counter\n\
             kal_out_of_bounds_queries {}\n\
             # HELP kal_hit_rate Fast path hit rate (0.0-1.0)\n\
             # TYPE kal_hit_rate gauge\n\
             kal_hit_rate {:.4}\n",
            self.fast_path_hits,
            self.slow_path_hits,
            self.invalid_returns,
            self.corrupted_entries,
            self.out_of_bounds_queries,
            self.hit_rate()
        )
    }
}
```

**Stratégie d'Export Observabilité** :

```rust
/// Callback pour export périodique des métriques
/// Utilisable pour push vers Prometheus, écriture fichier, ou autre
pub type MetricsCallback = Box<dyn Fn(TelemetrySnapshot) + Send + Sync>;

impl Provider {
    /// Configure un callback de métriques appelé périodiquement
    /// Note : Cette implémentation est simplifiée. Pour production,
    /// utiliser un système de scheduling externe (tokio, thread pool)
    pub fn set_metrics_callback(&mut self, callback: MetricsCallback) {
        // Stocké dans le Provider, appelé sur demande ou via timer externe
    }
}

// Exemple d'usage avec export HTTP
fn setup_metrics_endpoint(provider: Arc<Provider>) {
    use std::net::TcpListener;
    use std::io::Write;
    
    std::thread::spawn(move || {
        let listener = TcpListener::bind("127.0.0.1:9090").unwrap();
        
        for stream in listener.incoming() {
            if let Ok(mut stream) = stream {
                let telemetry = provider.get_telemetry();
                let metrics = telemetry.to_prometheus();
                
                let response = format!(
                    "HTTP/1.1 200 OK\r\n\
                     Content-Type: text/plain; version=0.0.4\r\n\
                     Content-Length: {}\r\n\
                     \r\n\
                     {}",
                    metrics.len(),
                    metrics
                );
                
                let _ = stream.write_all(response.as_bytes());
            }
        }
    });
}
```

**Stratégie de Logging** :

```
Logs de Corruption (JSON vers stderr) :
- Format : {"timestamp": "...", "event": "corruption_detected", ...}
- Rotation : gérée par l'application hôte (systemd, Docker, etc.)
- Pas de buffering : flush immédiat pour garantir traçabilité

Métriques (Prometheus) :
- Exposition HTTP sur port configurable (défaut 9090)
- Pas de rétention interne : Prometheus scrape périodique
- Compteurs atomiques → pas de lock, overhead minimal

Principe : Le runtime expose les données, l'infrastructure gère la collecte.
```
```

**Tests** :

```rust
#[test]
fn test_telemetry_corruption_tracking() {
    // Création fichier corrompu
    let mut data = create_valid_litu(2025, 1);
    
    // Injection corruption (season = 15)
    data[16] = 0xFF;
    data[17] = 0xFF;
    data[18] = 0xFF;
    data[19] = 0xFF;
    
    write_file("corrupt.kald", &data);
    
    let provider = Provider::new("corrupt.kald", "corrupt.lits", make_slow_path()).unwrap();
    let result = provider.get_day(2025, 1);
    
    assert_eq!(result.as_u32(), 0xFFFFFFFF);  // DayPacked::invalid()
    
    let telemetry = provider.get_telemetry();
    assert_eq!(telemetry.corrupted_entries, 1);
    assert_eq!(telemetry.invalid_returns, 0);  // corruption ≠ invalid_returns
}

#[test]
fn test_telemetry_hit_rates() {
    let provider = Provider::new("france.kald", "france.lits", make_slow_path()).unwrap();
    
    // 100 requêtes dans la plage
    for i in 0..100 {
        provider.get_day(2025 + (i % 10), 1 + (i % 365));
    }
    
    // 50 requêtes hors plage
    for i in 0..50 {
        provider.get_day(1500, 1);
    }
    
    let telemetry = provider.get_telemetry();
    assert_eq!(telemetry.fast_path_hits, 100);
    assert_eq!(telemetry.slow_path_hits, 0);
    assert_eq!(telemetry.out_of_bounds_queries, 50);
}
```

#### 3.2 FFI Durci avec Gestion d'Erreurs (Semaine 6, Jours 4-5)

**Fichier** : `liturgical-calendar-runtime/src/ffi.rs`

**API C Étendue** :

```c
// kal.h
typedef struct KalProvider KalProvider;

typedef struct {
    uint32_t value;
    uint32_t error_code;
} KalResult;

typedef struct {
    uint64_t fast_path_hits;
    uint64_t slow_path_hits;
    uint64_t invalid_returns;
    uint64_t corrupted_entries;
    uint64_t out_of_bounds_queries;
} KalTelemetry;

// Error codes
#define KAL_OK 0
#define KAL_INVALID_HANDLE 1
#define KAL_FILE_ERROR 2
#define KAL_INVALID_DAY 3
#define KAL_CORRUPTED_ENTRY 4
#define KAL_OUT_OF_BOUNDS 5

// API principale
KalProvider* kal_new(const char* data_path, const char* lang_path);
void kal_free(KalProvider* handle);

KalResult kal_get_day_checked(
    const KalProvider* handle,
    int16_t year,
    uint16_t day_of_year
);

uint32_t kal_get_day(
    const KalProvider* handle,
    int16_t year,
    uint16_t day_of_year
);

KalTelemetry kal_get_telemetry(const KalProvider* handle);
const char* kal_get_last_error(const KalProvider* handle);
```

**Implémentation Rust** :

```rust
use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::cell::RefCell;

pub struct Provider {
    // ... champs existants ...
    
    /// NOUVEAU : Dernier message d'erreur (thread-local)
    last_error: RefCell<Option<CString>>,
}

impl Provider {
    fn set_last_error(&self, msg: &str) {
        *self.last_error.borrow_mut() = Some(
            CString::new(msg).unwrap_or_else(|_| CString::new("Error message encoding failed").unwrap())
        );
    }
}

#[repr(C)]
pub struct KalResult {
    pub value: u32,
    pub error_code: u32,
}

#[no_mangle]
pub extern "C" fn kal_get_day_checked(
    handle: *const Provider,
    year: i16,
    day_of_year: u16,
) -> KalResult {
    if handle.is_null() {
        return KalResult {
            value: 0,
            error_code: 1,  // INVALID_HANDLE
        };
    }
    
    if day_of_year == 0 || day_of_year > 366 {
        let provider = unsafe { &*handle };
        provider.set_last_error(&format!("Invalid day_of_year: {}", day_of_year));
        return KalResult {
            value: 0,
            error_code: 3,  // INVALID_DAY
        };
    }
    
    let provider = unsafe { &*handle };
    let day = provider.get_day(year, day_of_year);
    
    if day.is_invalid() {
        let error_code = if year < 1583 || year > 4099 {
            provider.set_last_error(&format!("Year {} out of bounds", year));
            5  // OUT_OF_BOUNDS
        } else {
            provider.set_last_error("Corrupted entry or invalid day");
            4  // CORRUPTED_ENTRY
        };
        
        KalResult {
            value: 0,
            error_code,
        }
    } else {
        KalResult {
            value: day.as_u32(),
            error_code: 0,  // OK
        }
    }
}

#[no_mangle]
pub extern "C" fn kal_get_last_error(
    handle: *const Provider
) -> *const c_char {
    if handle.is_null() {
        return std::ptr::null();
    }
    
    let provider = unsafe { &*handle };
    provider.last_error.borrow()
        .as_ref()
        .map(|s| s.as_ptr())
        .unwrap_or(std::ptr::null())
}
```

**Tests C** :

```c
// tests/test_ffi.c
#include <assert.h>
#include <stdio.h>
#include "kal.h"

void test_invalid_day() {
    KalProvider* provider = kal_new("france.kald", "france.lits");
    
    KalResult result = kal_get_day_checked(provider, 2025, 0);
    assert(result.error_code == KAL_INVALID_DAY);
    assert(result.value == 0);
    
    const char* error = kal_get_last_error(provider);
    printf("Error: %s\n", error);
    
    kal_free(provider);
}

void test_out_of_bounds() {
    KalProvider* provider = kal_new("france.kald", "france.lits");
    
    KalResult result = kal_get_day_checked(provider, 1500, 1);
    assert(result.error_code == KAL_OUT_OF_BOUNDS);
    
    kal_free(provider);
}

int main() {
    test_invalid_day();
    test_out_of_bounds();
    printf("All FFI tests passed!\n");
    return 0;
}
```

#### 3.3 Metrics Phase 3

**Critères de Validation** :

- ✅ Runtime : charge france.kald en <50ms
- ✅ Fast Path : <100ns par requête
- ✅ Télémétrie : compteurs fonctionnels
- ✅ Corruption : détection + log JSON
- ✅ FFI : tests C passés (Valgrind clean)

**Livrables** :

- `liturgical-calendar-runtime-0.1.0` (crate + .so/.dylib)
- `kal.h` (header C)
- Tests FFI (C)

---

## Phase 4 : Tests de Robustesse (Semaine 8)

### Objectif

Garantir la robustesse production via fuzzing, cross-build, et tests d'intégration.

### Livrable Principal

**Suite CI/CD** : GitHub Actions avec determinism checking

### Tâches Détaillées

#### 4.1 Fuzzing (Jours 1-2)

**Objectif** : Garantir l'absence de panics et la gestion contrôlée des erreurs sur inputs aléatoires.

**Invariants Attendus** :
1. Aucun panic sur input arbitraire
2. Aucun undefined behavior (vérifié par MIRI)
3. Les erreurs retournées sont cohérentes avec le type d'input
4. Les compteurs de télémétrie s'incrémentent correctement

**Seuils Minimaux** :
- 10,000 mutations par target (header, full file)
- Coverage ≥80% des branches dans le code de validation
- 0 panics, 0 crashes, 0 UB détectés

**Corpus Initial** :

```
corpus/
├── valid_minimal.kald      # Header + 1 année valide
├── valid_france.kald       # 300 ans complet
├── empty.kald              # Fichier vide
├── truncated_header.kald   # Header incomplet (8 octets)
├── invalid_magic.kald      # Magic incorrect
├── invalid_version.kald    # Version = 99
├── unknown_flags.kald      # Flags = 0xFFFF
└── corrupted_data.kald     # Data body avec valeurs invalides
```

**Fichier** : `fuzz/fuzz_targets/kald_header.rs`

```rust
#![no_main]
use libfuzzer_sys::fuzz_target;
use liturgical_calendar_core::validate_header;

fuzz_target!(|data: &[u8]| {
    // INVARIANT : Aucun panic
    let _ = validate_header(data);
    
    // Note : Les erreurs sont attendues et acceptables
    // Ce qui est interdit : panics, UB, segfaults
});
```

**Fichier** : `fuzz/fuzz_targets/kald_full.rs`

```rust
#![no_main]
use libfuzzer_sys::fuzz_target;
use liturgical_calendar_runtime::Provider;
use std::io::Write;
use std::sync::atomic::{AtomicUsize, Ordering};

static ITERATION: AtomicUsize = AtomicUsize::new(0);

fuzz_target!(|data: &[u8]| {
    if data.len() < 1480 {  // Header + 1 année minimum
        return;
    }
    
    let iter = ITERATION.fetch_add(1, Ordering::Relaxed);
    let path = format!("/tmp/fuzz_{}_{}.kald", std::process::id(), iter);
    
    // Écriture du fichier
    if let Ok(mut file) = std::fs::File::create(&path) {
        let _ = file.write_all(data);
        drop(file);
        
        // Tentative de chargement
        // INVARIANT : Pas de panic même sur input corrompu
        let rules = HardcodedRuleProvider::new_roman_rite_ordinary();
        let slow_path = SlowPath::new(rules);
        if let Ok(provider) = Provider::new(&path, "dummy.lits", slow_path) {
            // Si le chargement réussit, tester quelques requêtes
            let _ = provider.get_day(2025, 1);
            let _ = provider.get_day(2025, 366);
            let _ = provider.get_day(1500, 1);  // Hors limites
            
            // Vérifier cohérence de la télémétrie
            let telemetry = provider.get_telemetry();
            let total = telemetry.fast_path_hits 
                + telemetry.slow_path_hits 
                + telemetry.out_of_bounds_queries
                + telemetry.invalid_returns;
            
            // INVARIANT : Au moins 3 requêtes comptabilisées
            assert!(total >= 3, "Telemetry inconsistent: total={}", total);
        }
        
        // Cleanup
        let _ = std::fs::remove_file(&path);
    }
});
```

**Exécution et Reporting** :

```bash
# Fuzzing header (rapide, focus sur validation)
$ cargo fuzz run kald_header -- \
    -max_total_time=300 \
    -print_final_stats=1 \
    corpus/

# Fuzzing full file (plus lent, teste tout le pipeline)
$ cargo fuzz run kald_full -- \
    -max_total_time=1800 \
    -print_final_stats=1 \
    -dict=fuzz/dict.txt \
    corpus/

# Coverage report
$ cargo fuzz coverage kald_full
$ cargo cov -- show target/*/release/kald_full \
    --format=html \
    --instr-profile=fuzz/coverage/kald_full/coverage.profdata \
    > coverage.html
```

**Dictionnaire (fuzz/dict.txt)** :

```
# Magic values
"KALD"
"XKALD"

# Version numbers
"\x01\x00"
"\x02\x00"

# Common year values
"\xE9\x07"  # 2025
"\x2C\x01"  # 300 years

# Flags
"\x00\x00"
"\xFF\xFF"

# Season/Color/Rank values (tous les valides et invalides)
"\x00"
"\x01"
"\x05"
"\x0F"
"\xFF"
```

**Critères de Succès Phase Fuzzing** :

- ✅ 10,000+ inputs testés par target
- ✅ 0 panics détectés
- ✅ 0 crashes (segfault, abort, etc.)
- ✅ Coverage ≥80% sur validation code
- ✅ Rapport de fuzzing documenté (stats, coverage)

#### 4.2 Cross-Build Determinism (Jour 3)

**Objectif** : Garantir que la Forge produit des fichiers bit-for-bit identiques sur différentes plateformes.

**Matrix CI** : Tests sur 4 targets représentatifs

```yaml
# Configuration des targets
# IMPORTANT : Endianness native = un .kald par architecture
targets:
  - runner: ubuntu-latest
    target: x86_64-unknown-linux-gnu
    endian: little
  
  - runner: ubuntu-latest
    target: aarch64-unknown-linux-gnu
    endian: little
    cross: true  # Utilise cross-rs
  
  - runner: macos-latest
    target: x86_64-apple-darwin
    endian: little
  
  - runner: macos-latest
    target: aarch64-apple-darwin
    endian: little
```

**Fichier** : `.github/workflows/determinism.yml`

```yaml
name: Cross-Build Determinism

on: [push, pull_request]

jobs:
  build-matrix:
    strategy:
      matrix:
        include:
          - os: ubuntu-latest
            target: x86_64-unknown-linux-gnu
            use_cross: false
          - os: ubuntu-latest
            target: aarch64-unknown-linux-gnu
            use_cross: true
          - os: macos-13
            target: x86_64-apple-darwin
            use_cross: false
          - os: macos-14
            target: aarch64-apple-darwin
            use_cross: false
    
    runs-on: ${{ matrix.os }}
    
    steps:
      - uses: actions/checkout@v3
      
      - uses: dtolnay/rust-toolchain@stable
        with:
          targets: ${{ matrix.target }}
      
      - name: Install cross (if needed)
        if: matrix.use_cross
        run: cargo install cross --git https://github.com/cross-rs/cross
      
      - name: Build forge
        run: |
          if [ "${{ matrix.use_cross }}" = "true" ]; then
            cross build --release --bin liturgical-calendar-forge --target ${{ matrix.target }}
          else
            cargo build --release --bin liturgical-calendar-forge --target ${{ matrix.target }}
          fi
      
      - name: Generate calendar
        run: |
          FORGE_BIN=./target/${{ matrix.target }}/release/liturgical-calendar-forge
          $FORGE_BIN build --config test.toml --output france-${{ matrix.target }}.kald
      
      - name: Compute hash
        id: hash
        run: |
          HASH=$(sha256sum france-${{ matrix.target }}.kald | awk '{print $1}')
          echo "hash=$HASH" >> $GITHUB_OUTPUT
          echo "$HASH" > hash-${{ matrix.target }}.txt
      
      - name: Run diagnostics
        run: |
          INSPECT_BIN=./target/${{ matrix.target }}/release/kald-inspect
          $INSPECT_BIN france-${{ matrix.target }}.kald --check > diagnostic-${{ matrix.target }}.txt
      
      - uses: actions/upload-artifact@v3
        with:
          name: build-${{ matrix.target }}
          path: |
            france-${{ matrix.target }}.kald
            hash-${{ matrix.target }}.txt
            diagnostic-${{ matrix.target }}.txt
  
  compare:
    needs: build-matrix
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/download-artifact@v3
      
      - name: Compare hashes within same endianness
        run: |
          # Tous les targets little-endian doivent avoir le même hash
          LINUX_X64=$(cat build-x86_64-unknown-linux-gnu/hash-x86_64-unknown-linux-gnu.txt)
          LINUX_ARM64=$(cat build-aarch64-unknown-linux-gnu/hash-aarch64-unknown-linux-gnu.txt)
          MACOS_X64=$(cat build-x86_64-apple-darwin/hash-x86_64-apple-darwin.txt)
          MACOS_ARM64=$(cat build-aarch64-apple-darwin/hash-aarch64-apple-darwin.txt)
          
          echo "Linux x86_64:  $LINUX_X64"
          echo "Linux aarch64: $LINUX_ARM64"
          echo "macOS x86_64:  $MACOS_X64"
          echo "macOS aarch64: $MACOS_ARM64"
          
          if [ "$LINUX_X64" = "$LINUX_ARM64" ] && \
             [ "$LINUX_ARM64" = "$MACOS_X64" ] && \
             [ "$MACOS_X64" = "$MACOS_ARM64" ]; then
            echo "✓ Determinism verified across all platforms"
            exit 0
          else
            echo "✗ Hash mismatch detected"
            exit 1
          fi
      
      - name: Verify diagnostics
        run: |
          # Vérifier que tous les diagnostics passent
          for target in x86_64-unknown-linux-gnu aarch64-unknown-linux-gnu \
                        x86_64-apple-darwin aarch64-apple-darwin; do
            if ! grep -q "✓ No corruptions detected" build-$target/diagnostic-$target.txt; then
              echo "✗ Corruption detected in $target build"
              cat build-$target/diagnostic-$target.txt
              exit 1
            fi
          done
          echo "✓ All diagnostics passed"
```

**Distribution Multi-Arch** :

Pour les déploiements production, distribuer un .kald par architecture :
```
releases/
├── france-x86_64-linux.kald       (little-endian)
├── france-aarch64-linux.kald      (little-endian, identique)
├── france-x86_64-darwin.kald      (little-endian, identique)
└── france-aarch64-darwin.kald     (little-endian, identique)
```

Dans la pratique, puisque tous sont little-endian et identiques, un seul fichier suffit avec renommage symbolique.

#### 4.3 Tests d'Intégration Complets (Jour 4)

**Fichier** : `tests/integration.rs`

```rust
#[test]
fn test_forge_runtime_identity_loop() {
    // Build
    let config = Config::load("test.toml").unwrap();
    let builder = CalendarBuilder::new(config).unwrap();
    let calendar = builder.build().unwrap();
    
    calendar.write_kald("test_loop.kald").unwrap();
    calendar.write_lits("test_loop.lits", "fr").unwrap();
    
    // Load
    let provider = Provider::new("test_loop.kald", "test_loop.lits", make_slow_path()).unwrap();
    
    // Verify 100 dates
    for year in 2025..2030 {
        for day in [1, 50, 100, 150, 200, 250, 300, 365] {
            let runtime = provider.get_day(year, day);
            let slow = provider.compute_slow(year, day)
                .map(|l| DayPacked::from(l))
                .unwrap_or_else(|_| DayPacked::invalid());
            
            assert_eq!(runtime.as_u32(), slow.as_u32(),
                "Divergence at {}-{:03}", year, day);
        }
    }
}

#[test]
fn test_corruption_injection_handling() {
    // Create valid file
    let mut data = create_valid_litu(2025, 10);
    
    // Inject 10 corruptions
    for i in 0..10 {
        let offset = 16 + (i * 100 * 4);
        data[offset] = 0xFF;
        data[offset + 1] = 0xFF;
        data[offset + 2] = 0xFF;
        data[offset + 3] = 0xFF;
    }
    
    write_file("corrupt.kald", &data);
    
    let provider = Provider::new("corrupt.kald", "corrupt.lits", make_slow_path()).unwrap();
    
    // Query all 3660 days (10 years)
    let mut invalid_count = 0;
    for year in 2025..2035 {
        for day in 1..=366 {
            let result = provider.get_day(year, day);
            if result.is_invalid() {
                invalid_count += 1;
            }
        }
    }
    
    // Verify telemetry
    let telemetry = provider.get_telemetry();
    assert_eq!(telemetry.corrupted_entries, 10);
    assert!(invalid_count >= 10);  // Au moins les 10 corrompus
}
```

#### 4.4 Metrics Phase 4

**Critères de Validation** :

- ✅ Fuzzing : 0 panics sur 10k inputs
- ✅ Cross-build : SHA-256 identique (3 OS)
- ✅ Integration : 100% tests passés
- ✅ Coverage : ≥90% (global)

**Livrables** :

- CI/CD configuré (GitHub Actions)
- Rapport de fuzzing
- Certification determinism

---

## Phase 5 : Documentation & Packaging (Semaine 9)

### Objectif

Production-ready : documentation complète, exemples, et packaging.

### Livrable Principal

**Release v1.0.0** : Crates.io + GitHub Release

### Tâches

#### 5.1 Documentation Rustdoc (Jours 1-2)

- Documentation API complète (100% public items)
- Exemples inline
- Invariants critiques documentés

#### 5.2 Exemples Multiples (Jour 3)

**Exemples** :

```
examples/
├── rust_basic.rs          # Usage Rust simple
├── rust_advanced.rs       # Avec télémétrie
├── c_basic.c              # Usage C FFI
├── python_ctypes.py       # Via ctypes
└── diagnostics.sh         # Scripts d'inspection
```

#### 5.3 Packaging (Jours 4-5)

- `cargo package` pour crates.io
- GitHub Release avec binaires
- Docker image (optionnel)

**Metrics** :

- ✅ Documentation : 100% public API
- ✅ Exemples : 5+ fonctionnels
- ✅ Release : publiée et testée

---

## Phase 6 : Optimisation & Profiling (Semaine 10)

### Objectif

Optimisation finale et validation performance.

### Livrable Principal

**Rapport de Performance** : Benchmarks documentés

### Tâches

#### 6.1 Profiling (Jours 1-2)

```bash
$ perf record --call-graph=dwarf \
    ./target/release/bench --bench runtime
$ perf report
```

#### 6.2 Codegen Audit (Jour 3)

```bash
$ cargo asm --release liturgical_calendar_runtime::provider::get_day
```

**Vérifications** :

- Inlining réussi (pas de CALL)
- Pas de bound checks (optimisés)
- Accès mémoire linéaire

#### 6.3 Optimisations Finales (Jours 4-5)

- Annotations `#[inline(always)]` critiques
- SIMD si applicable
- Cache prefetch hints

**Metrics** :

- ✅ Fast Path : <80ns (gain 20%)
- ✅ Slow Path : <10µs
- ✅ Codegen : vérifié manuellement

---

## Phase 7 : Tests Critiques Additionnels (Intégré Phase 4)

### Tests à Ajouter (Ordre Prioritaire)

Ces tests complètent les phases précédentes et doivent être intégrés en Phase 4.

#### 7.1 Tests Header Flags (Priorité Haute)

**Fichier** : `tests/header_validation.rs`

```rust
#[test]
fn test_header_unknown_flags() {
    let header_bytes = [
        b'K', b'A', b'L', b'D',  // Magic
        0x01, 0x00,              // Version 1
        0xE9, 0x07,              // Start 2025
        0x2C, 0x01,              // Count 300
        0x01, 0x00,              // Flags 0x0001 (INCONNU)
        0x00, 0x00, 0x00, 0x00,  // Padding
    ];
    
    let result = validate_header(&header_bytes);
    assert!(matches!(result, Err(HeaderError::UnsupportedFlags { .. })));
}

#[test]
fn test_header_padding_non_zero() {
    let header_bytes = [
        b'K', b'A', b'L', b'D',
        0x01, 0x00,
        0xE9, 0x07,
        0x2C, 0x01,
        0x00, 0x00,
        0xFF, 0x00, 0x00, 0x00,  // Padding invalide
    ];
    
    let result = validate_header(&header_bytes);
    assert!(matches!(result, Err(HeaderError::InvalidPadding(_))));
}

#[test]
fn test_header_file_size_mismatch() {
    // Créer header valide mais fichier tronqué
    let mut data = create_valid_header(2025, 300);
    data.truncate(1000);  // Tronquer
    
    write_file("truncated.kald", &data);
    let result = Provider::new("truncated.kald", "truncated.lits", make_slow_path());
    assert!(matches!(result, Err(RuntimeError::Io(IoError::CorruptedFile { .. }))));
}
```

#### 7.2 Tests FeastID Interop (Priorité Haute)

**Fichier** : `tests/registry_interop.rs`

```rust
#[test]
fn test_feast_id_interop_10k_allocations() {
    // Forge 1 : France
    let mut registry_fr = FeastRegistry::new();
    
    for i in 0..10_000 {
        let id = registry_fr.allocate_next(2, 1).unwrap();
        registry_fr.register(id, format!("Saint FR {}", i)).unwrap();
    }
    
    // Export
    let export = registry_fr.export_scope(2, 1);
    assert_eq!(export.allocations.len(), 10_000);
    
    // Forge 2 : Allemagne
    let mut registry_de = FeastRegistry::new();
    let import_result = registry_de.import(export);
    
    assert!(import_result.is_ok());
    let report = import_result.unwrap();
    assert_eq!(report.imported, 10_000);
    assert_eq!(report.collisions.len(), 0);
    
    // Allocation allemande ne doit pas collisionner
    for i in 0..1000 {
        let de_id = registry_de.allocate_next(2, 1).unwrap();
        assert!(de_id >= 0x212710);  // Au-delà des 10k français
        assert!(!registry_fr.has_feast_id(de_id));
    }
}

#[test]
fn test_registry_collision_detection() {
    let mut registry = FeastRegistry::new();
    registry.register(0x210001, "Saint A".to_string()).unwrap();

    let export = RegistryExport {
        scope: 2,
        category: 1,
        version: 1,
        allocations: vec![(0x210001, "Saint B".to_string())],
    };

    // Conforme spec §3.3 : Ok même en présence de collision.
    // La collision est dans ImportReport::collisions, non dans Err.
    let result = registry.import(export);
    assert!(result.is_ok());

    let report = result.unwrap();
    assert_eq!(report.collisions.len(), 1);
    assert_eq!(report.collisions[0].feast_id, 0x210001);
    assert_eq!(report.collisions[0].existing, "Saint A");
    assert_eq!(report.collisions[0].incoming, "Saint B");
}
```

#### 7.3 Tests Telemetry Under Load (Priorité Moyenne)

**Fichier** : `benches/telemetry_load.rs`

```rust
#[bench]
fn bench_telemetry_mixed_load(b: &mut Bencher) {
    let provider = Provider::new("france.kald", "france.lits", make_slow_path()).unwrap();
    let mut rng = thread_rng();
    
    b.iter(|| {
        // 70% fast path, 20% slow path, 10% invalides
        let roll = rng.gen_range(0..100);
        
        if roll < 70 {
            // Fast path
            provider.get_day(rng.gen_range(2025..2325), rng.gen_range(1..=365))
        } else if roll < 90 {
            // Slow path (hors range)
            provider.get_day(rng.gen_range(1583..2025), rng.gen_range(1..=365))
        } else {
            // Invalide
            provider.get_day(2025, rng.gen_range(367..=500))
        }
    });
    
    // Vérification cohérence télémétrie
    let telemetry = provider.get_telemetry();
    let total = telemetry.fast_path_hits 
        + telemetry.slow_path_hits 
        + telemetry.invalid_returns 
        + telemetry.out_of_bounds_queries;
    
    assert!(total > 0, "Telemetry not incremented");
}

#[test]
fn test_telemetry_atomic_consistency() {
    use std::sync::Arc;
    use std::thread;
    
    let provider = Arc::new(Provider::new("france.kald", "france.lits", make_slow_path()).unwrap());
    let mut handles = vec![];
    
    // 10 threads × 1000 requêtes
    for _ in 0..10 {
        let p = provider.clone();
        handles.push(thread::spawn(move || {
            for i in 0..1000 {
                p.get_day(2025, (i % 365) + 1);
            }
        }));
    }
    
    for h in handles {
        h.join().unwrap();
    }
    
    let telemetry = provider.get_telemetry();
    assert_eq!(telemetry.fast_path_hits, 10_000);
}
```

#### 7.4 Tests FFI Contract (Priorité Haute)

**Fichier** : `tests/test_ffi.c`

```c
#include <assert.h>
#include <stdio.h>
#include <string.h>
#include "kal.h"

void test_handle_null() {
    KalResult result = kal_get_day_checked(NULL, 2025, 1);
    assert(result.error_code == KAL_INVALID_HANDLE);
    assert(result.value == 0);
    printf("✓ NULL handle handled\n");
}

void test_invalid_day_of_year() {
    KalProvider* provider = kal_new("france.kald", "france.lits");
    
    // day_of_year = 0
    KalResult r1 = kal_get_day_checked(provider, 2025, 0);
    assert(r1.error_code == KAL_INVALID_DAY);
    
    // day_of_year > 366
    KalResult r2 = kal_get_day_checked(provider, 2025, 367);
    assert(r2.error_code == KAL_INVALID_DAY);
    
    const char* err = kal_get_last_error(provider);
    assert(err != NULL);
    assert(strlen(err) > 0);
    
    kal_free(provider);
    printf("✓ Invalid day_of_year handled\n");
}

void test_corrupted_file() {
    // Créer fichier corrompu
    FILE* f = fopen("corrupt_test.kald", "wb");
    unsigned char data[1480] = {0xFF};
    memcpy(data, "KALD", 4);
    fwrite(data, 1, 1480, f);
    fclose(f);
    
    KalProvider* provider = kal_new("corrupt_test.kald", "france.lits");
    
    if (provider != NULL) {
        KalResult result = kal_get_day_checked(provider, 2025, 1);
        
        // Doit retourner erreur ou invalide
        assert(result.error_code != KAL_OK || result.value == 0);
        
        kal_free(provider);
    }
    
    printf("✓ Corrupted file handled\n");
}

void test_telemetry() {
    KalProvider* provider = kal_new("france.kald", "france.lits");
    
    // Faire quelques requêtes
    for (int i = 1; i <= 100; i++) {
        kal_get_day(provider, 2025, i);
    }
    
    KalTelemetry telemetry = kal_get_telemetry(provider);
    assert(telemetry.fast_path_hits == 100);
    
    kal_free(provider);
    printf("✓ Telemetry functional\n");
}

int main() {
    test_handle_null();
    test_invalid_day_of_year();
    test_corrupted_file();
    test_telemetry();
    
    printf("\n✓ All FFI contract tests passed\n");
    return 0;
}
```

**Compilation et Exécution** :

```bash
$ gcc -o test_ffi tests/test_ffi.c -L./target/release -lliturgical_calendar_runtime
$ LD_LIBRARY_PATH=./target/release ./test_ffi
```

#### 7.5 Intégration dans CI

**Fichier** : `.github/workflows/tests.yml`

```yaml
name: Tests Suite

on: [push, pull_request]

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - run: cargo test --all-features
  
  ffi-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - run: cargo build --release --lib
      - run: gcc -o test_ffi tests/test_ffi.c -L./target/release -lliturgical_calendar_runtime
      - run: LD_LIBRARY_PATH=./target/release ./test_ffi
  
  fuzzing:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - run: cargo install cargo-fuzz
      - run: cargo fuzz run kald_header -- -max_total_time=300
      - run: cargo fuzz run kald_full -- -max_total_time=600
```

---

## Milestones & Critères de Succès

### Milestone 1 : Core + Diagnostics (Fin Semaine 2)

- ✅ `liturgical-calendar-core` : 100% tests passés
- ✅ `kald-inspect` : binaire fonctionnel
- ✅ Coverage ≥90%
- ✅ `is_sunday` implémenté et testé

### Milestone 2 : Forge Production (Fin Semaine 5)

- ✅ `liturgical-calendar-forge` : binaire complet
- ✅ Déterminisme : SHA-256 identique
- ✅ Registry : import/export fonctionnel
- ✅ france.kald généré (300 ans)

### Milestone 3 : Runtime Robuste (Fin Semaine 7)

- ✅ `liturgical-calendar-runtime` : bibliothèque complète
- ✅ Télémétrie : fonctionnelle
- ✅ FFI : tests C passés
- ✅ Corruption handling : validé

### Milestone 4 : Tests Robustesse (Fin Semaine 8)

- ✅ Fuzzing : 0 panics
- ✅ Cross-build : determinism vérifié
- ✅ Integration tests : 100% passés
- ✅ CI/CD : automatisé

### Milestone 5 : Production Release (Fin Semaine 9)

- ✅ Documentation : 100% complète
- ✅ Exemples : 5+ fonctionnels
- ✅ Release v1.0.0 : publiée

### Milestone 6 : Optimisation (Fin Semaine 10)

- ✅ Performance : cibles atteintes
- ✅ Codegen : audité
- ✅ Profiling : documenté

---

## Résumé des Corrections Appliquées

| #   | Point Hardening                    | Correction                                       | Phase | Criticité    |
| --- | ---------------------------------- | ------------------------------------------------ | ----- | ------------ |
| 1   | Validation header flags            | Rejet strict bits inconnus + politique migration | 1.3   | **Haute**    |
| 2   | Corruption silencieuse             | API Result + télémétrie + logs JSON + timestamp | 3.1   | **Haute**    |
| 3   | is_sunday manquant                 | Tomohiko Sakamoto + lookup tables optimisées    | 1.2   | **Haute**    |
| 4   | Endianness non documentée          | Diagnostic kald-inspect + matrix CI cross-arch   | 1.4   | **Moyenne**  |
| 5   | FeastID collisions                 | Registry déterministe BTreeMap + import/export   | 2.1   | **Moyenne**  |
| 6   | FFI sans gestion erreur            | KalResult + last_error + tests C            | 3.2   | **Haute**    |
| 7   | Pas de fuzzing                     | Harness + corpus + invariants + seuils 10k       | 4.1   | **Haute**    |
| 8   | Cross-build non testé              | CI matrix 4 targets + determinism SHA-256        | 4.2   | **Haute**    |
| 9   | Observabilité manquante            | Télémétrie + export Prometheus + logs JSON       | 3.1   | **Haute**    |
| 10  | BTreeMap (déjà corrigé)            | Maintenu dans v2.0 (déterminisme garanti)        | 2.2   | **Critique** |
| 11  | Contention registry                | Supprimé — modèle &mut self suffisant (Forge mono-thread) | 2.1 | **N/A** |
| 12  | Tests manquants                    | Header/FeastID/Telemetry/FFI tests ajoutés      | 7     | **Haute**    |
| A1  | Import collisions : Err vs Ok      | Aligné spec §3.3 — Ok(report), collisions non fatales | 2.1 | **Critique** |
| A2  | FeastRegistry Arc\<Mutex\> vs &mut self | Aligné spec §3.2 — modèle simple mono-thread | 2.1 | **Critique** |
| A3  | Bug décembre day_of_year_to_month_day | Corrigé : soustraction itérative (conforme spec §4.3) | 1.2 | **Haute** |
| A4  | Signature day_of_year_to_month_day | Alignée spec §4.3 : (u16, bool) au lieu de (i32, u16) | 1.2 | **Moyenne** |
| B1  | log_corruption signature : CorruptionInfo vs u32 brut | Aligné spec §7 — (year, day_of_year, packed: u32), reconstruction interne | 3.1 | **Critique** |
| B2  | log_corruption body : champs fantômes info.year/day_of_year | Corrigé spec §7 — accès aux paramètres locaux year/day_of_year | 7   | **Critique** |
| B3  | CalendarBuilder::build : Error générique + variant inconnue | Roadmap — RuntimeError + DomainError::YearOutOfBounds conforme §9.1 | 2.2 | **Haute**    |
| B4  | CalendarBuilder.cache : BTreeMap<_, Day> vs insert DayPacked | Roadmap — unifié à DayPacked, conversion Day→DayPacked au point de calcul | 2.2 | **Haute**    |

---

## Extensions Futures (v2.x)

### v2.1 : Compression

- Flag compression dans header
- Support ZSTD du Data Body

### v2.2 : Rites Extraordinaires

- Flag rite dans header
- Deux Slow Paths (Ordinaire/Extraordinaire)

### v2.3 : Calendriers Orthodoxes

- Calendrier Julien
- Algorithme Pâques orthodoxe

### v2.4 : API REST

- Serveur HTTP léger
- Endpoints RESTful

---

## Metrics de Qualité Finales

### Code Quality

- **Coverage** : ≥90% (cargo-tarpaulin)
- **Clippy** : 0 warnings
- **Unsafe** : <100 lignes (justifiées)
- **Documentation** : 100% public API

### Performance

- **Build Time** : <10s (300 ans)
- **Load Time** : <50ms
- **Fast Path** : <80ns
- **Slow Path** : <10µs

### Robustness

- **Fuzzing** : 0 panics (10k inputs)
- **Determinism** : SHA-256 cross-platform
- **FFI** : Valgrind clean
- **Corruption** : Détection + logging

### Documentation

- **API Docs** : 100%
- **Examples** : ≥5
- **Invariants** : Documentés

---

**Fin de la Roadmap v2.0**
