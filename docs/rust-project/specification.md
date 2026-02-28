# Spécification Technique : Liturgical Calendar v2.0

**Statut** : Canonique / Production-Ready  
**Architecture** : Shared-Core / DOD / Fast-Slow Path / AOT  
**Langage Domaine** : Latin (Strictement Canonique)  
**Déterminisme** : Bit-for-bit reproductible  
**Date de Révision** : 2026-02-19  
**Version** : 2.0

---

## Philosophie Architecturale

**Principe Fondamental** : liturgical-calendar est un moteur déterministe AOT capable de produire un artefact annuel figé appelé **Kalendarium**, sérialisé au format `.kald` (magic `KALD`).

Le système est **complet et autonome**, capable de calculer le calendrier liturgique pour n'importe quelle année grégorienne canonique (1583-4099) via son **Slow Path algorithmique**.

Le **Fast Path** (fichier `.kald`) n'est pas un cache obligatoire ni un fallback : c'est une **optimisation spatiale et temporelle délibérée** pour une plage de travail spécifique choisie par l'utilisateur.

**Conception en Deux Niveaux** :

1. **Slow Path (Citoyen de Première Classe)** :
   - Calcul algorithmique des règles liturgiques
   - Couvre l'intégralité du calendrier grégorien (1583-4099)
   - Latence : <10µs par jour
   - Aucune dépendance externe

2. **Fast Path (Optimisation Optionnelle)** :
   - Pré-calcul AOT d'une fenêtre temporelle choisie
   - Typiquement : -50/+300 ans autour de l'année courante
   - Latence : <100ns par jour (gain ×100)
   - Fichier `.kald` : luxe de performance pour les années critiques

**Cas d'Usage** :

- **Application mobile** : Fichier `.kald` intégré couvrant 2000-2100 (optimisation pour utilisateurs contemporains), Slow Path pour requêtes historiques/futures
- **Serveur liturgique** : Fenêtre glissante régénérée annuellement (année courante ±50 ans), Slow Path pour archives/projections
- **Recherche historique** : Pas de Fast Path, Slow Path uniquement (1583-2025)
- **Calendrier perpétuel** : Fast Path 1900-2200 (ère moderne complète), Slow Path pour hors-limites

**L'utilisateur choisit sa plage d'optimisation. Le système continue de fonctionner pour toutes les autres années.**

---

## 1. Vocabulaire du Domaine (Ubiquitous Language)

Toutes les définitions utilisent le Latin Canonique. Les Enums Rust sont annotées `#[repr(u8)]` pour garantir la représentation binaire exacte.

### 1.1 Types Fondamentaux (Correction Audit #2 - Séparation Logic/Packed)

**IMPORTANT** : Le système utilise deux représentations distinctes pour garantir la séparation des responsabilités :

```rust
/// Représentation LOGIQUE pour la Forge et le Slow Path
/// Structure riche avec validations, conversions, et métadonnées
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Day {
    pub precedence: Precedence,
    pub nature: Nature,
    pub color: Color,
    pub season: Season,
    pub feast_id: u32,
}

/// Représentation PACKED pour le Runtime (Fast Path)
/// Transparente au u32 pour zero-cost abstraction
#[repr(transparent)]
#[derive(Copy, Clone, Debug, PartialEq, Eq)]
pub struct DayPacked(u32);

/// Information détaillée sur une corruption de Data Body
///
/// Retournée par DayPacked::try_from_u32 pour permettre un log structuré.
/// Le champ offset est rempli par le Provider au moment de l'accès.
#[derive(Debug, Clone)]
pub struct CorruptionInfo {
    /// Valeur u32 brute lue dans le Data Body
    pub packed_value: u32,
    /// Nom du champ invalide ("precedence", "nature", "color", "season", "reserved")
    pub invalid_field: &'static str,
    /// Valeur numérique du champ invalide
    pub invalid_value: u8,
    /// Offset dans le fichier .kald (rempli par le Provider)
    pub offset: Option<usize>,
}

impl DayPacked {
    /// Construction sécurisée avec validation des bits
    ///
    /// Retourne CorruptionInfo détaillé en cas d'échec, permettant un log
    /// structuré sans allocation dans le happy path.
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

    /// Extraction du u32 brut (zero-cost)
    #[inline(always)]
    pub fn as_u32(&self) -> u32 {
        self.0
    }

    /// Conversion vers la forme logique (pour debugging/affichage)
    pub fn to_logic(&self) -> Result<Day, CorruptionInfo> {
        Day::try_from_u32(self.0)
            .map_err(|e| CorruptionInfo {
                packed_value: self.0,
                invalid_field: e.field_name(),
                invalid_value: e.field_value(),
                offset: None,
            })
    }

    /// Crée un jour marqué comme invalide (pour erreurs)
    ///
    /// INVARIANT : 0xFFFFFFFF est hors domaine valide.
    /// Décomposé selon le layout DayPacked v2.0 :
    ///   Precedence bits [31:28] = 15 → hors domaine (max = 12), rejeté par try_from_u8.
    ///   Nature bits [27:25] = 7 → hors domaine (max = 4), rejeté par try_from_u8.
    /// Aucune entrée liturgique valide ne peut produire cette valeur.
    /// Pas de collision possible avec une entrée décodable.
    ///
    /// NE PAS utiliser 0x00000000 : décode en (TriduumSacrum, Solemnitas, Albus, TempusOrdinarium, id=0),
    /// valeur sémantiquement valide — ambiguïté fatale pour la détection de corruption.
    pub fn invalid() -> Self {
        Self(0xFFFFFFFF)
    }

    /// Teste si ce DayPacked est la sentinelle d'erreur
    #[inline(always)]
    pub fn is_invalid(&self) -> bool {
        self.0 == 0xFFFFFFFF
    }
}

impl From<Day> for DayPacked {
    fn from(logic: Day) -> Self {
        Self(logic.into())
    }
}

impl From<Day> for u32 {
    fn from(day: Day) -> Self {
        ((day.precedence as u32) << 28)
            | ((day.nature as u32) << 25)
            | ((day.color as u32) << 22)
            | ((day.season as u32) << 19)
            | (day.feast_id & 0x3FFFF)
    }
}
```

**Justification de la Séparation** :

| Aspect             | `Day`         | `DayPacked`        |
| ------------------ | ---------------------------- | ---------------------------- |
| **Usage**          | Forge, Slow Path, calculs    | Runtime Fast Path uniquement |
| **Taille**         | ≥ 20 octets (struct riche)   | 4 octets (transparent)       |
| **Validation**     | Stricte à la construction    | Déjà validé par Forge        |
| **Conversions**    | Riches (JSON, display, etc.) | Minimales (u32 brut)         |
| **Évolution v2.x** | Extensible (nouveaux champs) | Figée (contrat binaire)      |

### 1.2 Color (3 bits)

Représentation des couleurs liturgiques selon les normes post-Vatican II.

```rust
#[repr(u8)]
#[derive(Copy, Clone, Debug, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub enum Color {
    Albus     = 0,  // Blanc
    Rubeus    = 1,  // Rouge
    Viridis   = 2,  // Vert
    Violaceus = 3,  // Violet
    Roseus    = 4,  // Rose
    Niger     = 5,  // Noir (défunts)
    // 6, 7 réservés pour extensions futures
}

impl Color {
    /// Construction sécurisée depuis u8 avec validation
    pub fn try_from_u8(val: u8) -> Result<Self, DomainError> {
        match val {
            0 => Ok(Self::Albus),
            1 => Ok(Self::Rubeus),
            2 => Ok(Self::Viridis),
            3 => Ok(Self::Violaceus),
            4 => Ok(Self::Roseus),
            5 => Ok(Self::Niger),
            _ => Err(DomainError::InvalidColor(val)),
        }
    }
}
```

> **Normalisation à la Forge** : `normalize_color(input: &str)` vit dans `liturgical-calendar-forge` (§5), pas dans `core`. Elle utilise `to_lowercase()` (allocation heap) et retourne `RegistryError`. Le crate `core` n'a pas cette dépendance.

### 1.3 Precedence (4 bits) et Nature (3 bits)

Le modèle v2.0 découple strictement deux axes orthogonaux.

**Axe ordinal : `Precedence` (4 bits)**

Force d'éviction. Comparaison purement numérique (`u32 >> 28`). Ordre total non cyclique. Une valeur numérique plus faible représente une force d'éviction plus élevée.

*Tabella dierum liturgicorum — NALC 1969. Ordre figé. Aucune modification autorisée après freeze v2.0.*

| Valeur | Niveau Canonique |
| ------ | ---------------- |
| 0 | Triduum Sacrum |
| 1 | Nativitas, Epiphania, Ascensio, Pentecostes |
| 2 | Dominicae Adventus, Quadragesimae, Paschales |
| 3 | Feria IV Cinerum; Hebdomada Sancta |
| 4 | Sollemnitates Domini, BMV, Sanctorum in Calendario Generali |
| 5 | Sollemnitates propriae |
| 6 | Festa Domini in Calendario Generali |
| 7 | Dominicae per annum |
| 8 | Festa BMV et Sanctorum in Calendario Generali |
| 9 | Festa propria |
| 10 | Feriae Adventus (17–24 Dec), Octava Nativitatis |
| 11 | Memoriae obligatoriae |
| 12 | Feriae per annum; Memoriae ad libitum |

```rust
#[repr(u8)]
#[derive(Copy, Clone, Debug, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub enum Precedence {
    TriduumSacrum                      = 0,
    SollemnitatesFixaeMaior            = 1,
    DominicaePrivilegiataeMaior        = 2,
    FeriaePrivilegiataeMaior           = 3,
    SollemnitatesGenerales            = 4,
    SollemnitatesPropria               = 5,
    FestaDomini                        = 6,
    DominicaePerAnnum                  = 7,
    FestaBMVEtSanctorumGenerales       = 8,
    FestaPropria                       = 9,
    FeriaeAdventusEtOctavaNativitatis  = 10,
    MemoriaeObligatoriae               = 11,
    FeriaePerAnnumEtMemoriaeAdLibitum  = 12,
    // 13-15 réservés
}

impl Precedence {
    pub fn try_from_u8(val: u8) -> Result<Self, DomainError> {
        match val {
            0  => Ok(Self::TriduumSacrum),
            1  => Ok(Self::SollemnitatesFixaeMaior),
            2  => Ok(Self::DominicaePrivilegiataeMaior),
            3  => Ok(Self::FeriaePrivilegiataeMaior),
            4  => Ok(Self::SollemnitatesGenerales),
            5  => Ok(Self::SollemnitatesPropria),
            6  => Ok(Self::FestaDomini),
            7  => Ok(Self::DominicaePerAnnum),
            8  => Ok(Self::FestaBMVEtSanctorumGenerales),
            9  => Ok(Self::FestaPropria),
            10 => Ok(Self::FeriaeAdventusEtOctavaNativitatis),
            11 => Ok(Self::MemoriaeObligatoriae),
            12 => Ok(Self::FeriaePerAnnumEtMemoriaeAdLibitum),
            _  => Err(DomainError::InvalidPrecedence(val)),
        }
    }
}
```

**Axe sémantique : `Nature` (3 bits)**

Typologie rituelle de l'entité liturgique. La Nature ne dicte jamais la force d'éviction. Une Feria peut posséder une Precedence supérieure à une Memoria (ex : Feria IV Cinerum, Precedence=3, est supérieure à toute Memoria, Precedence=11 ou 12). Ce découplage est la justification structurelle du modèle 2D.

> **Dominica** : n'est pas une Nature. Dominica est une classe canonique de précédence. Sa Nature structurelle est `Feria`. Sa force d'éviction est encodée par `Precedence::DominicaePerAnnum` (7) ou `Precedence::DominicaePrivilegiataeMaior` (2) selon le temps liturgique.

```rust
#[repr(u8)]
#[derive(Copy, Clone, Debug, PartialEq, Eq, Hash)]
pub enum Nature {
    Solemnitas    = 0,
    Festum        = 1,
    Memoria       = 2,
    Feria         = 3,
    Commemoratio  = 4,
    // 5-7 réservés
}

impl Nature {
    pub fn try_from_u8(val: u8) -> Result<Self, DomainError> {
        match val {
            0 => Ok(Self::Solemnitas),
            1 => Ok(Self::Festum),
            2 => Ok(Self::Memoria),
            3 => Ok(Self::Feria),
            4 => Ok(Self::Commemoratio),
            _ => Err(DomainError::InvalidNature(val)),
        }
    }
}
```

### 1.4 Season (3 bits)

États liturgiques du calendrier. L'indice 0 représente l'état par défaut (Temps Ordinaire). Champ cache AOT — bits [21:19] du layout DayPacked v2.0.

```rust
#[repr(u8)]
#[derive(Copy, Clone, Debug, PartialEq, Eq, Hash)]
pub enum Season {
    TempusOrdinarium    = 0,  // Temps Ordinaire (défaut)
    TempusAdventus      = 1,  // Avent
    TempusNativitatis   = 2,  // Temps de Noël
    TempusQuadragesimae = 3,  // Carême
    TriduumPaschale     = 4,  // Triduum Pascal
    TempusPaschale      = 5,  // Temps Pascal
    DiesSancti          = 6,  // Semaine Sainte (Rameaux-Mercredi)
    // 7 réservé (3 bits, valeur max = 7)
}

impl Season {
    /// Construction sécurisée depuis u8 avec validation
    pub fn try_from_u8(val: u8) -> Result<Self, DomainError> {
        match val {
            0 => Ok(Self::TempusOrdinarium),
            1 => Ok(Self::TempusAdventus),
            2 => Ok(Self::TempusNativitatis),
            3 => Ok(Self::TempusQuadragesimae),
            4 => Ok(Self::TriduumPaschale),
            5 => Ok(Self::TempusPaschale),
            6 => Ok(Self::DiesSancti),
            _ => Err(DomainError::InvalidSeason(val)),
        }
    }
}
```

**Frontières Temporelles (Représentation DOD - Correction Audit Dates)** :

```rust
/// Représentation interne optimisée pour calculs CPU
/// IMPORTANT : Utilise u16 (DayOfYear 1-366) au lieu de structures Date complexes
/// pour éviter l'overhead de bibliothèques comme chrono
#[derive(Copy, Clone, Debug)]
pub struct SeasonBoundaries {
    pub advent_start: u16,        // Jour de l'année (1-366)
    pub christmas_start: u16,     // 25 déc : jour 359 (année commune) ou 360 (bissextile)
    pub epiphany_end: u16,        // Baptême du Seigneur
    pub ash_wednesday: u16,       // Pâques - 46 jours
    pub palm_sunday: u16,         // Pâques - 7 jours
    pub holy_thursday: u16,       // Pâques - 3 jours
    pub easter_sunday: u16,       // Comput de Pâques
    pub pentecost: u16,           // Pâques + 49 jours
}

impl SeasonBoundaries {
    /// Calcule les frontières pour une année donnée
    /// Retourne None si l'année est hors limites (< 1583 ou > 4099)
    pub fn compute(year: i32) -> Option<Self> {
        if year < 1583 || year > 4099 {
            return None;
        }

        let easter = compute_easter(year);

        Some(Self {
            advent_start: compute_advent_start(year),
            christmas_start: if is_leap_year(year) { 360 } else { 359 },  // 25 déc : j.359 ou j.360
            epiphany_end: compute_baptism_of_lord(year),
            ash_wednesday: easter.saturating_sub(46),
            palm_sunday: easter.saturating_sub(7),
            holy_thursday: easter.saturating_sub(3),
            easter_sunday: easter,
            pentecost: easter + 49,
        })
    }
}

/// Calcul de Pâques (Algorithme de Meeus/Jones/Butcher)
fn compute_easter(year: i32) -> u16 {
    let a = year % 19;
    let b = year / 100;
    let c = year % 100;
    let d = b / 4;
    let e = b % 4;
    let f = (b + 8) / 25;
    let g = (b - f + 1) / 3;
    let h = (19 * a + b - d - g + 15) % 30;
    let i = c / 4;
    let k = c % 4;
    let l = (32 + 2 * e + 2 * i - h - k) % 7;
    let m = (a + 11 * h + 22 * l) / 451;
    let month = (h + l - 7 * m + 114) / 31;
    let day = ((h + l - 7 * m + 114) % 31) + 1;

    // Conversion mois/jour → jour de l'année
    let days_before_month = match month {
        3 => 31 + 28 + if is_leap_year(year) { 1 } else { 0 },
        4 => 31 + 28 + 31 + if is_leap_year(year) { 1 } else { 0 },
        _ => unreachable!(),
    };

    (days_before_month + day) as u16
}

/// Détermine si une année est bissextile (calendrier grégorien)
#[inline]
fn is_leap_year(year: i32) -> bool {
    (year % 4 == 0) && (year % 100 != 0 || year % 400 == 0)
}

/// Calcule le premier dimanche de l'Avent pour une année donnée
///
/// L'Avent commence le dimanche le plus proche du 30 novembre.
/// Retourne le jour de l'année (1-366).
fn compute_advent_start(year: i32) -> u16 {
    // Implémentation : roadmap section 1.2
    // Principe : trouver le dimanche le plus proche du 30 novembre (j.334)
    // puis reculer de 3 semaines (4e dimanche avant Noël)
    todo!("roadmap §1.2")
}

/// Calcule le jour de la Fête du Baptême du Seigneur (fin du Temps de Noël)
///
/// Retourne le jour de l'année (1-366).
fn compute_baptism_of_lord(year: i32) -> u16 {
    // Implémentation : roadmap section 1.2
    // Principe : dimanche après le 6 janvier (Épiphanie), ou lundi 7 si le
    // dimanche serait le 7 ou 8 janvier
    todo!("roadmap §1.2")
}
```

---

## 2. Format Binaire (.kald)

### 2.1 Structure Header (16 octets - Modifié avec Flags)

**Représentation Logique** :

```rust
/// Représentation logique du header (pas de layout mémoire direct)
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Header {
    pub magic: [u8; 4],      // "KALD" (0x4B414C44)
    pub version: u16,        // Version du format (actuellement 1)
    pub start_year: i16,     // Année de départ (2025 pour france.kald)
    pub year_count: u16,     // Nombre d'années couvertes (300 pour france.kald)
    pub flags: u16,          // Flags d'extension
    pub _padding: [u8; 4],   // Strict 0x00
}

impl Header {
    /// Désérialise un header depuis 16 octets bruts
    /// 
    /// IMPORTANT : Utilise l'endianness native (from_ne_bytes).
    /// Les fichiers .kald sont spécifiques à l'architecture de build.
    /// 
    /// SÉCURITÉ : Pas de comportement indéfini (UB) lié à l'alignement.
    /// Portable sur toutes les architectures (ARM, RISC-V, x86, etc.).
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
    
    /// Sérialise le header en 16 octets bruts (endianness native)
    pub fn to_bytes(&self) -> [u8; 16] {
        let mut bytes = [0u8; 16];
        
        bytes[0..4].copy_from_slice(&self.magic);
        bytes[4..6].copy_from_slice(&self.version.to_ne_bytes());
        bytes[6..8].copy_from_slice(&self.start_year.to_ne_bytes());
        bytes[8..10].copy_from_slice(&self.year_count.to_ne_bytes());
        bytes[10..12].copy_from_slice(&self.flags.to_ne_bytes());
        bytes[12..16].copy_from_slice(&self._padding);
        
        bytes
    }
}
```

**Politique Endianness** :

- **Native Build Endianness** : Les fichiers `.kald` utilisent l'endianness de la machine de build
- **Justification** : Gain de performance au runtime (pas de byte-swap), déploiement ciblé par architecture
- **Distribution** : Un fichier `.kald` par architecture cible (en pratique, peu-endian universel actuellement)
- **Détection Runtime** : L'outil `kald-inspect` vérifie la plausibilité du `start_year` pour détecter les mismatches
```

**Flags d'Extension (bits 0-15)** :

```
Bit 0     : Compression activée (0 = non, 1 = ZSTD)        [réservé v2.1]
Bit 1     : Checksums inclus (0 = non, 1 = CRC32)           [réservé v2.1]
Bit 2-3   : Réservé pour rites (00 = Ordinaire, 01 = Extraordinaire) [réservé v2.2]
Bit 4-15  : Réservé pour extensions futures
```

> **v1 : tous les flags sont refusés.** `KNOWN_FLAGS_V1 = 0x0000` — tout fichier présentant un flag non nul est rejeté au chargement (`UnsupportedFlags`). Les bits ci-dessus documentent les extensions planifiées pour v2+, non des fonctionnalités actives.

**Validation Stricte** :

```rust
/// Valide et désérialise un header depuis un mmap
/// 
/// SÉCURITÉ :
/// - Pas d'UB lié à l'alignement (désérialisation explicite)
/// - Validation stricte de tous les champs
/// - Détection des corruptions et mismatches
///
/// PARAMÈTRE : `&[u8]` générique — pas de dépendance à memmap2 dans ce crate.
/// L'appelant extrait le slice depuis son Mmap via `&mmap[..]` avant d'appeler.
/// VISIBILITÉ : `pub` — requis pour le fuzz target et les tests d'intégration
/// (`use liturgical_calendar_core::validate_header`).
pub fn validate_header(bytes: &[u8]) -> Result<Header, HeaderError> {
    if bytes.len() < 16 {
        return Err(HeaderError::FileTooSmall);
    }

    // Désérialisation sans UB (pas de cast de pointeur)
    let header = Header::from_bytes(&bytes[0..16])?;

    // Validation magic
    if &header.magic != b"KALD" {
        return Err(HeaderError::InvalidMagic(header.magic));
    }

    // Validation version
    if header.version != 1 {
        return Err(HeaderError::UnsupportedVersion(header.version));
    }
    
    // Validation flags (rejet strict des bits inconnus)
    const KNOWN_FLAGS_V1: u16 = 0x0000;
    if (header.flags & !KNOWN_FLAGS_V1) != 0 {
        return Err(HeaderError::UnsupportedFlags {
            found: header.flags,
            known: KNOWN_FLAGS_V1,
            unknown_bits: header.flags & !KNOWN_FLAGS_V1,
        });
    }

    // Validation padding (doit être strictement 0x00)
    if header._padding != [0, 0, 0, 0] {
        return Err(HeaderError::InvalidPadding(header._padding));
    }

    // Validation range années
    if header.start_year < 1583 || header.start_year > 4099 {
        return Err(HeaderError::YearOutOfBounds(header.start_year));
    }

    if header.year_count == 0 || header.year_count > 2516 {
        return Err(HeaderError::InvalidYearCount(header.year_count));
    }
    
    // Détection heuristique de mismatch endianness
    // Si start_year semble aberrant, probable endianness inversé
    if header.start_year < 1000 || header.start_year > 5000 {
        eprintln!(
            "⚠️  WARNING: start_year={} looks implausible, possible endianness mismatch",
            header.start_year
        );
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

### 2.2 Data Body (366 u32 × N années)

**Layout Strict** :

```
Offset   : Contenu
──────────────────────────────────
0x0000   : Header (16 octets)
0x0010   : Année[0], Jour 1 (u32)
0x0014   : Année[0], Jour 2 (u32)
...
0x05C4   : Année[0], Jour 366 (u32)
0x05C8   : Année[1], Jour 1 (u32)
...
```

**Taille Fichier** :

```rust
const HEADER_SIZE: usize = 16;
const YEAR_SIZE: usize = 366 * 4;  // 366 u32 = 1464 octets

fn compute_file_size(year_count: u16) -> usize {
    HEADER_SIZE + (year_count as usize * YEAR_SIZE)
}

// Exemple : france.kald (2025-2324, 300 ans)
// = 16 + (300 * 1464) = 439,216 octets
```

**Endianness (Documentation Runtime)** :

```rust
/// IMPORTANT : Le format .kald utilise l'endianness NATIVE de la machine de build.
///
/// Justification :
/// - Gain de performance : pas de conversion byte-swap au runtime
/// - Déploiement ciblé : les .kald sont spécifiques à l'architecture cible
/// - Production-ready : les builds cross-platform utilisent des .kald séparés
///
/// Implémentation :
/// - Header : désérialisé avec from_ne_bytes() (pas de cast de pointeur)
/// - Data Body : u32 lus avec from_ne_bytes()
/// - Sérialisation : to_ne_bytes() partout
///
/// Détection Runtime (pour diagnostics) :
pub fn detect_endianness_mismatch(header: &Header) -> bool {
    // Heuristique : le start_year doit être plausible
    // Si aberrant, probable mismatch d'endianness
    header.start_year < 1000 || header.start_year > 5000
}

/// Utilitaire de diagnostic
pub fn diagnose_file(path: &str) -> Result<DiagnosticReport, IoError> {
    let file = File::open(path)?;
    let mmap = unsafe { Mmap::map(&file)? };
    
    // Désérialisation sans UB
    let header = Header::from_bytes(&mmap[0..16])?;

    let report = DiagnosticReport {
        file_size: mmap.len(),
        magic: header.magic,
        version: header.version,
        start_year: header.start_year,
        year_count: header.year_count,
        flags: header.flags,
        endianness_ok: !detect_endianness_mismatch(&header),
        system_endian: if cfg!(target_endian = "little") { "little" } else { "big" },
    };

    Ok(report)
}
```

**Convention de Build** :

```toml
# Cargo.toml - Spécification des targets
[package.metadata.kald-build]
targets = [
    "x86_64-unknown-linux-gnu",      # Little-endian
    "aarch64-unknown-linux-gnu",     # Little-endian
    "x86_64-apple-darwin",           # Little-endian
    "aarch64-apple-darwin",          # Little-endian
    # Pour big-endian, utiliser des builds séparés
]
```

### 2.3 Bitpacking Layout — DayPacked (u32)

**Layout Normatif (v2.0 — figé)** :

| Bits     | Champ      | Taille  | Description                                                        |
| -------- | ---------- | ------- | ------------------------------------------------------------------ |
| [31..28] | Precedence | 4 bits  | Axe ordinal (0–12). Z-Index strict. Comparaison purement entière.  |
| [27..25] | Nature     | 3 bits  | Axe sémantique (Solemnitas, Festum, Memoria, Feria, Commemoratio). |
| [24..22] | Color      | 3 bits  | Couleur liturgique finale résolue en Forge.                        |
| [21..19] | Season     | 3 bits  | Cache AOT — rendu O(1). Valeurs 0–6, 7 réservé.                   |
| [18]     | Reserved   | 1 bit   | Inactif en v2.0. Positionné à 0 par la Forge.                     |
| [17..0]  | FeastID    | 18 bits | Identifiant de fête (0–262 143).                               |

**Extraction (Runtime)** :

```rust
impl Day {
    pub fn try_from_u32(packed: u32) -> Result<Self, DomainError> {
        let precedence_bits = ((packed >> 28) & 0xF) as u8;
        let nature_bits     = ((packed >> 25) & 0x7) as u8;
        let color_bits      = ((packed >> 22) & 0x7) as u8;
        let season_bits     = ((packed >> 19) & 0x7) as u8;
        // Bit [18] : Reserved — doit être 0
        let reserved_bit    = (packed >> 18) & 0x1;
        let feast_id        = packed & 0x3FFFF;
        if reserved_bit != 0 {
            return Err(DomainError::ReservedBitSet);
        }

        Ok(Self {
            precedence: Precedence::try_from_u8(precedence_bits)?,
            nature:     Nature::try_from_u8(nature_bits)?,
            color:      Color::try_from_u8(color_bits)?,
            season:     Season::try_from_u8(season_bits)?,
            feast_id,
        })
    }
}
```

---

### 2.4 Invariants Structurels (v2.0 — Freeze)

### INV-1 : Comparaison Ordinale

- La Precedence est l'unique axe de résolution des collisions.
- Comparaison purement entière : `(packed_a >> 28) < (packed_b >> 28)`.
- Ordre total, non cyclique.
- Valeur numérique plus faible = force d'éviction plus élevée.
- Aucune logique sémantique n'intervient dans la collision.

### INV-2 : Immutabilité du Z-Index

- La Tabella (13 niveaux, §1.3) est figée après freeze v2.0.
- Aucune modification de l'ordre 0–12 n'est autorisée.
- Toute extension future doit utiliser des valeurs hors de la plage 0–12, via migration majeure.

### INV-3 : Séparation des Axes

- `Nature ≠ Precedence`. Les deux axes sont orthogonaux.
- La Nature ne dicte jamais la force d'éviction.
- Cas normatif : `Feria IV Cinerum` possède `Precedence=3` (FeriaePrivilegiataeMaior), supérieure à toute `Memoria` (`Precedence=11`), bien que sa Nature soit `Feria`.
- `Dominica` n'est pas une Nature. Sa Nature est `Feria`. Sa force d'éviction est encodée par `Precedence::DominicaePerAnnum` (7) ou `Precedence::DominicaePrivilegiataeMaior` (2).

### INV-4 : Forge comme Producteur Unique

- Le fichier `.kald` est généré exclusivement par la Forge (Slow Path, AOT).
- Le runtime (Fast Path) est strictement en lecture.
- Aucune mutation, recalcul liturgique, de saison, de couleur ou de précédence n'est autorisé au runtime.

### INV-5 : Unicité des Commémorations

- La Forge garantit au maximum une seule Commemoratio par jour.
- Le runtime ne gère aucune liste de collisions.
- Toute collision complexe est résolue en AOT.

### INV-6 : Redondance Contrôlée — Season

- Le champ `Season` (bits [21:19]) est une matérialisation AOT volontaire.
- Il garantit un rendu O(1) sans calcul de frontières temporelles au runtime.
- Il est un cache structurel, non une donnée dérivable au runtime.

### INV-7 : Bit Reserved

- Le bit [18] est inactif en v2.0.
- La Forge le positionne à 0.
- Aucun comportement ne dépend de ce bit en v2.0.
- `try_from_u32` retourne `DomainError::ReservedBitSet` si ce bit est à 1.

---

## 3. FeastID Registry (Correction Audit #1 - Collisions)

### 3.1 Espace d'Allocation Hiérarchique

**Structure des FeastID (18 bits)** :

```
Bits 17-16 (2 bits) : Scope       (0=Universal, 1=Regional, 2=National, 3=Local)
Bits 15-12 (4 bits) : Category    (0=Temporal, 1=Sanctoral, 2=Marian, etc.)
Bits 11-0  (12 bits): Sequential  (0-4095 par scope/category)
```

Capacité totale : 262 144 FeastID (valeurs 0 à 262 143). Largement suffisant pour tout sanctoral universel, régional et local prévisible.

**Exemple d'Allocation** :

```
Universal/Temporal  : 0x00000 - 0x00FFF
Universal/Sanctoral : 0x01000 - 0x01FFF
Regional/Sanctoral  : 0x09000 - 0x09FFF
National/Sanctoral  : 0x11000 - 0x11FFF
Local/Sanctoral     : 0x19000 - 0x19FFF
```

### 3.2 Registry Canonique

```rust
use std::collections::BTreeMap;
use std::fs::File;
use std::io::Write;
use serde::{Serialize, Deserialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FeastRegistry {
    /// Mappage FeastID → Nom canonique
    pub allocations: BTreeMap<u32, String>,

    /// Compteurs par scope/category pour allocation séquentielle
    pub next_id: BTreeMap<(u8, u8), u16>,
}

impl FeastRegistry {
    /// Charge le registry depuis un fichier JSON canonique
    pub fn load(path: &str) -> Result<Self, IoError> {
        let file = File::open(path)?;
        let registry: Self = serde_json::from_reader(file)?;
        Ok(registry)
    }

    /// Sauvegarde le registry (déterministe via BTreeMap)
    pub fn save(&self, path: &str) -> Result<(), RegistryError> {
        let file = File::create(path)?;
        serde_json::to_writer_pretty(file, self)?;
        Ok(())
    }

    /// Alloue le prochain ID disponible pour un scope/category
    pub fn allocate_next(&mut self, scope: u8, category: u8) -> Result<u32, RegistryError> {
        if scope > 3 || category > 15 {
            return Err(RegistryError::InvalidScopeCategory { scope, category });
        }

        let key = (scope, category);
        let next = self.next_id.entry(key).or_insert(0);

        if *next == 0x1000 {
            return Err(RegistryError::FeastIDExhausted { scope, category });
        }

        let feast_id = ((scope as u32) << 16)
            | ((category as u32) << 12)
            | (*next as u32);

        *next += 1;

        Ok(feast_id)
    }

    /// Enregistre une allocation avec nom canonique
    pub fn register(&mut self, feast_id: u32, name: String) -> Result<(), RegistryError> {
        if self.allocations.contains_key(&feast_id) {
            return Err(RegistryError::FeastIDCollision(feast_id));
        }

        self.allocations.insert(feast_id, name);
        Ok(())
    }
}
```

### 3.3 Import/Export pour Interopérabilité

```rust
#[derive(Debug, Serialize, Deserialize)]
pub struct RegistryExport {
    pub scope: u8,
    pub category: u8,
    pub version: u16,              // Version du format d'export (actuellement 1)
    pub allocations: Vec<(u32, String)>,
}

/// Rapport d'importation retourné par FeastRegistry::import()
#[derive(Debug, Clone)]
pub struct ImportReport {
    pub imported: usize,           // Entrées nouvellement importées
    pub skipped: usize,            // Entrées ignorées (déjà présentes, identiques)
    pub collisions: Vec<CollisionInfo>,  // Conflits de noms pour un même FeastID
}

/// Détail d'un conflit de FeastID lors de l'import
#[derive(Debug, Clone)]
pub struct CollisionInfo {
    pub feast_id: u32,
    pub existing: String,          // Nom déjà enregistré localement
    pub incoming: String,          // Nom du fichier importé
}

impl FeastRegistry {
    /// Export d'un scope/category pour partage entre forges
    pub fn export_scope(&self, scope: u8, category: u8) -> RegistryExport {
        let prefix = ((scope as u32) << 16) | ((category as u32) << 12);
        let mask = 0x3F000u32;  // Bits [17:12] : Scope (2 bits) + Category (4 bits)

        let allocations: Vec<(u32, String)> = self
            .allocations
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

    /// Import avec détection de collision
    ///
    /// Retourne ImportReport si tout s'est bien passé (collisions incluses dans le rapport).
    /// Retourne Err uniquement pour les erreurs I/O ou d'intégrité structurelle.
    pub fn import(&mut self, export: RegistryExport) -> Result<ImportReport, RegistryError> {
        let mut report = ImportReport {
            imported: 0,
            skipped: 0,
            collisions: Vec::new(),
        };

        for (feast_id, name) in export.allocations {
            // Vérification collision
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
            .filter(|id| (**id & 0x3F000u32) == ((export.scope as u32) << 16 | (export.category as u32) << 12))
            .map(|id| (id & 0xFFF) as u16)
            .max()
            .unwrap_or(0);

        self.next_id.insert(key, max_seq + 1);

        Ok(report)
    }
}
```

**Workflow de Partage** :

```bash
# Forge France : Export des saints nationaux
$ liturgical-calendar-forge registry export --scope 2 --category 1 --output france_sanctoral.json

# Forge Allemagne : Import pour éviter collisions
$ liturgical-calendar-forge registry import --file france_sanctoral.json

# Allocation allemande (commencera à partir du dernier ID français + 1)
$ liturgical-calendar-forge registry allocate --scope 2 --category 1 --name "St. Boniface"
```

---

## 4. Slow Path (Calcul Algorithmique Complet)

### Statut Architectural

Le Slow Path est le **cœur algorithmique complet** du système, capable de calculer n'importe quelle date liturgique sans dépendances externes. Il n'est pas un "fallback" ou un "plan B" : c'est le système de référence canonique.

**Caractéristiques** :

- **Complétude** : Couvre 1583-4099 (calendrier grégorien complet)
- **Autonomie** : Aucune donnée pré-calculée requise
- **Déterminisme** : Résultats identiques au Fast Path (validé par tests)
- **Performance** : <10µs par jour (acceptable pour la plupart des usages)

**Relation Fast/Slow** :

Le Fast Path est une **optimisation pré-calculée** d'une fenêtre du Slow Path. Leur identité est garantie par construction (la Forge utilise le Slow Path pour générer le `.kald`). Le choix entre les deux est une **décision de performance**, pas de correction fonctionnelle.

### 4.1 Architecture Stratifiée

Le Slow Path est organisé en couches hiérarchiques pour gérer les règles liturgiques complexes.

```rust
pub struct SlowPath {
    /// Règles temporelles (calendrier civil → calendrier liturgique)
    temporal: TemporalLayer,

    /// Règles sanctorales (fêtes fixes des saints)
    sanctoral: SanctoralLayer,

    /// Règles de précédence (résolution des conflits)
    precedence: PrecedenceResolver,
}

impl SlowPath {
    /// Calcule la liturgie d'un jour donné
    pub fn compute(&self, year: i16, day_of_year: u16) -> Result<Day, DomainError> {
        // 1. Calcul des frontières de saisons
        let boundaries = SeasonBoundaries::compute(year as i32)
            .ok_or(DomainError::YearOutOfBounds(year))?;

        // 2. Détermination de la saison
        let season = self.temporal.determine_season(day_of_year, &boundaries)?;

        // 3. Recherche des fêtes candidates
        let temporal_feast = self.temporal.get_feast(year, day_of_year, &boundaries);
        let sanctoral_feast = self.sanctoral.get_feast(year, day_of_year);

        // 4. Résolution de précédence
        let (precedence, nature, color, feast_id) = self.precedence.resolve(
            year,
            &season,
            temporal_feast,
            sanctoral_feast,
            day_of_year,
            &boundaries,
        )?;

        Ok(Day {
            precedence,
            nature,
            season,
            color,
            feast_id,
        })
    }
}
```

### 4.2 Temporal Layer (Règles Calendrier Liturgique)

```rust
/// Données d'une fête liturgique dans le hot path du Slow Path
///
/// TYPE COPY intentionnel : pas d'allocation dans get_feast/get_day.
/// Le nom canonique n'est PAS ici — il est dans le StringProvider (.lits).
/// Le moteur n'a besoin que de l'identifiant, de la précédence, de la nature et de la couleur.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct FeastDefinition {
    pub id: u32,               // FeastID (18 bits, voir section 3)
    pub precedence: Precedence,
    pub nature: Nature,
    pub color: Color,
}

pub struct TemporalLayer {
    /// Table des fêtes mobiles indexées par jour-de-l'année relatif à Pâques
    /// Clé : u16 (day_of_year absolu, calculé à partir des SeasonBoundaries)
    /// Pas de String : zéro allocation dans le hot path
    moveable_feasts: BTreeMap<u16, FeastDefinition>,
}

impl TemporalLayer {
    pub fn determine_season(
        &self,
        day_of_year: u16,
        boundaries: &SeasonBoundaries,
    ) -> Result<Season, DomainError> {
        // NOTE : SeasonBoundaries est par année civile. Le wrap Avent/Noël
        // (les jours post-24 déc d'une année étant en Avent de la suivante)
        // doit être géré en amont par le Provider, pas ici.
        if day_of_year >= boundaries.advent_start && day_of_year < boundaries.christmas_start {
            Ok(Season::TempusAdventus)
        } else if day_of_year >= boundaries.christmas_start && day_of_year <= boundaries.epiphany_end {
            Ok(Season::TempusNativitatis)
        } else if day_of_year >= boundaries.ash_wednesday && day_of_year < boundaries.palm_sunday {
            Ok(Season::TempusQuadragesimae)
        } else if day_of_year >= boundaries.palm_sunday && day_of_year < boundaries.holy_thursday {
            Ok(Season::DiesSancti)
        } else if day_of_year >= boundaries.holy_thursday && day_of_year < boundaries.easter_sunday {
            // Triduum : Jeudi Saint → Samedi Saint (easter-3 à easter-1 inclus)
            Ok(Season::TriduumPaschale)
        } else if day_of_year >= boundaries.easter_sunday && day_of_year <= boundaries.pentecost {
            // Temps Pascal : Pâques (inclus) → Pentecôte (incluse)
            Ok(Season::TempusPaschale)
        } else {
            Ok(Season::TempusOrdinarium)
        }
    }

    pub fn get_feast(
        &self,
        year: i16,
        day_of_year: u16,
        boundaries: &SeasonBoundaries,
    ) -> Option<FeastDefinition> {
        // Recherche de fêtes temporelles (Pâques, Pentecôte, etc.)
        if day_of_year == boundaries.easter_sunday {
            Some(FeastDefinition {
                id: 0x00001,
                precedence: Precedence::TriduumSacrum,
                nature: Nature::Solemnitas,
                color: Color::Albus,
            })
        } else if day_of_year == boundaries.pentecost {
            Some(FeastDefinition {
                id: 0x00002,
                precedence: Precedence::SollemnitatesFixaeMaior,
                nature: Nature::Solemnitas,
                color: Color::Rubeus,
            })
        } else {
            None
        }
    }
}
```

### 4.3 Sanctoral Layer (Fêtes Fixes)

```rust
pub struct SanctoralLayer {
    /// Fêtes fixes indexées par (mois, jour)
    fixed_feasts: BTreeMap<(u8, u8), Vec<FeastDefinition>>,
}

impl SanctoralLayer {
    pub fn get_feast(&self, year: i16, day_of_year: u16) -> Option<FeastDefinition> {
        let (month, day) = day_of_year_to_month_day(day_of_year, is_leap_year(year as i32));

        self.fixed_feasts
            .get(&(month, day))
            .and_then(|feasts| feasts.first().cloned())
    }
}

fn day_of_year_to_month_day(day_of_year: u16, is_leap: bool) -> (u8, u8) {
    let days_per_month = if is_leap {
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
```

### 4.4 Precedence Resolver

**Règles de Résolution (Ordre Strict)** :

La résolution est une comparaison purement numérique sur l'axe `Precedence`. Valeur plus faible = force d'éviction plus élevée. Aucune logique sémantique n'intervient dans la collision.

```rust
pub struct PrecedenceResolver;

impl PrecedenceResolver {
    /// Résout les conflits entre fêtes selon la Tabella dierum liturgicorum
    pub fn resolve(
        &self,
        year: i16,
        season: &Season,
        temporal: Option<FeastDefinition>,
        sanctoral: Option<FeastDefinition>,
        day_of_year: u16,
        boundaries: &SeasonBoundaries,
    ) -> Result<(Precedence, Nature, Color, u32), DomainError> {
        // Résolution : sélection du candidat à Precedence numérique minimale.
        // En cas d'égalité : le candidat temporal prime sur le sanctoral.
        let winner = match (temporal, sanctoral) {
            (Some(t), Some(s)) => {
                if (t.precedence as u8) <= (s.precedence as u8) { t } else { s }
            }
            (Some(t), None) => t,
            (None, Some(s)) => s,
            (None, None) => {
                // Feria par défaut : Precedence selon saison
                let prec = default_precedence(season, is_sunday(year as i32, day_of_year));
                return Ok((prec, Nature::Feria, season_default_color(season), 0));
            }
        };

        Ok((winner.precedence, winner.nature, winner.color, winner.id))
    }
}

fn default_precedence(season: &Season, is_sunday: bool) -> Precedence {
    if is_sunday {
        match season {
            Season::TempusAdventus
            | Season::TempusQuadragesimae
            | Season::TempusPaschale => Precedence::DominicaePrivilegiataeMaior,
            _ => Precedence::DominicaePerAnnum,
        }
    } else {
        match season {
            Season::TempusAdventus => Precedence::FeriaeAdventusEtOctavaNativitatis,
            Season::TempusNativitatis => Precedence::FeriaeAdventusEtOctavaNativitatis,
            Season::TempusQuadragesimae | Season::DiesSancti => Precedence::FeriaePrivilegiataeMaior,
            Season::TriduumPaschale => Precedence::TriduumSacrum,
            _ => Precedence::FeriaePerAnnumEtMemoriaeAdLibitum,
        }
    }
}

fn season_default_color(season: &Season) -> Color {
    match season {
        Season::TempusOrdinarium => Color::Viridis,
        Season::TempusAdventus => Color::Violaceus,
        Season::TempusNativitatis => Color::Albus,
        Season::TempusQuadragesimae => Color::Violaceus,
        Season::TriduumPaschale => Color::Albus,
        Season::TempusPaschale => Color::Albus,
        Season::DiesSancti => Color::Rubeus,
    }
}

fn is_sunday(year: i32, day_of_year: u16) -> bool {
    // Implémentation complète : algorithme de Tomohiko Sakamoto
    // Voir roadmap section 1.2 pour le code détaillé et les tests.
    // Signature : (year: i32, day_of_year: u16) — l'année est requise par l'algorithme.
    todo!("roadmap §1.2")
}
```

---

## 5. Forge (Génération AOT de Fenêtre Optimisée)

### Philosophie

La Forge génère un **Kalendarium** (fichier `.kald`) pour une **fenêtre temporelle choisie par l'utilisateur**. Ce choix est stratégique : il détermine quelles années bénéficient de l'optimisation Fast Path (<100ns) vs Slow Path (<10µs).

**Paramètres de Fenêtre** :

```toml
# config.toml
[calendar]
start_year = 2025      # Début de la fenêtre optimisée
year_count = 300       # Durée de la fenêtre (2025-2324)

# Exemples de stratégies :
# - Application mobile contemporaine : 2000-2100 (100 ans)
# - Serveur avec fenêtre glissante : année_courante-50 à +250
# - Archive historique complète : 1583-2025 (442 ans)
# - Calendrier perpétuel moderne : 1900-2200 (300 ans)
```

**Contraintes** :

- Fenêtre dans [1583, 4099] (calendrier grégorien valide)
- Taille fichier : 16 octets + (year_count × 1464 octets)
- Génération : ~10s pour 300 ans sur machine standard

**Hors fenêtre** : Le runtime bascule automatiquement sur le Slow Path, transparent pour l'utilisateur.

### 5.1 Configuration des Règles Liturgiques

**IMPORTANT** : La spécification technique v2.0 définit l'**architecture et les contrats** du système, mais **ne spécifie pas le contenu liturgique exhaustif**.

**Responsabilité de l'Opérateur** :

L'opérateur doit fournir la configuration complète des règles liturgiques via fichiers de configuration (TOML, JSON, ou YAML) comprenant :

1. **Règles Temporelles** :
   - Fêtes mobiles en relation avec Pâques (Ascension, Pentecôte, etc.)
   - Règles de déplacement (Saint-Joseph, Annonciation sur dimanche, etc.)
   - Semaines liturgiques (Carême, Avent, etc.)

2. **Règles Sanctorales** :
   - Fêtes fixes des saints (sanctoral universel, national, diocésain)
   - Patronages et célébrations locales
   - Mémoires obligatoires et facultatives

3. **Règles de Précédence** :
   - Ordre de priorité entre célébrations concurrentes
   - Exceptions liturgiques (Triduum Pascal, etc.)

4. **Fêtes Votives** (facultatif) :
   - Messes votives selon les circonstances
   - Communs des saints

**Format de Configuration** (exemple schématique) :

```toml
# config.toml
[calendar]
start_year = 2025
year_count = 300

# Règles temporelles
[[temporal_rules]]
type = "relative_to_easter"
name = "Ascension"
offset_days = 39
precedence = 1
nature = "solemnitas"
color = "albus"

[[temporal_rules]]
type = "displacement"
name = "Saint Joseph"
base_date = { month = 3, day = 19 }
displaced_if = ["palm_sunday", "holy_week"]
displaced_to = "next_available"

# Règles sanctorales
[[sanctoral_feasts]]
date = { month = 1, day = 1 }
name = "Sainte Marie, Mère de Dieu"
precedence = 4
nature = "solemnitas"
color = "albus"
scope = "universal"

[[sanctoral_feasts]]
date = { month = 7, day = 14 }
name = "Fête nationale de la France"
precedence = 9
nature = "festum"
color = "albus"
scope = "national"
region = "FR"
```

**Implémentation** :

Le `CalendarBuilder` charge cette configuration et construit les couches `TemporalLayer` et `SanctoralLayer` :

```rust
impl CalendarBuilder {
    pub fn new(config: Config) -> Result<Self, RuntimeError> {
        let feast_registry = FeastRegistry::load(&config.registry_path)?;
        
        // Construction du Slow Path depuis la configuration
        let slow_path = SlowPath::from_config(&config)?;
        
        Ok(Self {
            config,
            feast_registry,
            slow_path,
            cache: BTreeMap::new(),
        })
    }
}

impl SlowPath {
    /// Construit le Slow Path depuis une configuration
    pub fn from_config(config: &Config) -> Result<Self, RuntimeError> {
        let temporal = TemporalLayer::from_rules(&config.temporal_rules)?;
        let sanctoral = SanctoralLayer::from_feasts(&config.sanctoral_feasts)?;
        let precedence = PrecedenceResolver::from_config(&config.precedence)?;
        
        Ok(Self {
            temporal,
            sanctoral,
            precedence,
        })
    }
}
```

**Fonctions de normalisation (Forge uniquement — std requis)**

Ces fonctions vivent dans `liturgical-calendar-forge/src/config.rs`. Elles opèrent sur des `&str` issus de fichiers TOML/YAML et sont exclues du crate `core` (`no_std`). Le type d'erreur est `RegistryError` (couche forge), pas `DomainError`.

```rust
// liturgical-calendar-forge/src/config.rs
// std disponible ici — alloc autorisée

/// Convertit une chaîne de configuration en Color liturgique.
///
/// Accepte les variantes latines, anglaises et françaises pour
/// faciliter la saisie par les opérateurs.
///
/// NOTE ARCHITECTURALE : cette fonction est dans forge, pas dans core.
/// `to_lowercase()` alloue. `core` (no_std) n'en a pas besoin :
/// il n'opère que sur des u8 via Color::try_from_u8.
fn normalize_color(input: &str) -> Result<Color, RegistryError> {
    match input.to_lowercase().as_str() {
        "albus"    | "white" | "blanc"  => Ok(Color::Albus),
        "rubeus"   | "red"   | "rouge"  => Ok(Color::Rubeus),
        "viridis"  | "green" | "vert"   => Ok(Color::Viridis),
        "violaceus"| "violet"           => Ok(Color::Violaceus),
        "roseus"   | "rose"             => Ok(Color::Roseus),
        "niger"    | "black" | "noir"   => Ok(Color::Niger),
        _ => Err(RegistryError::UnknownColorString(input.to_string())),
    }
}

/// Convertit une chaîne de configuration en Precedence liturgique.
fn normalize_precedence(input: u8) -> Result<Precedence, RegistryError> {
    Precedence::try_from_u8(input)
        .map_err(|_| RegistryError::InvalidPrecedenceValue(input))
}

/// Convertit une chaîne de configuration en Nature liturgique.
fn normalize_nature(input: Option<&str>) -> Result<Nature, RegistryError> {
    match input {
        Some("solemnitas")   => Ok(Nature::Solemnitas),
        Some("festum")       => Ok(Nature::Festum),
        Some("memoria")      => Ok(Nature::Memoria),
        Some("feria") | None => Ok(Nature::Feria),
        Some("commemoratio") => Ok(Nature::Commemoratio),
        Some(other) => Err(RegistryError::UnknownNatureString(other.to_string())),
    }
}
```

**Hors Scope v1.0** :

La spécification v2.0 se concentre sur :
- Architecture du système
- Format binaire `.kald`
- Contrats des APIs
- Pipeline de génération

Le contenu liturgique exhaustif (toutes les fêtes votives, règles de déplacement complexes, etc.) sera fourni dans des **fichiers de configuration séparés** maintenus par l'opérateur ou la communauté.

**Extensions Futures** :

- v2.x : Bibliothèque de configurations pré-établies (Rite Romain Ordinaire, Extraordinaire, etc.)
- v3.x : Éditeur visuel de règles liturgiques
- v4.x : Validation automatique contre le Calendrier Romain Général

#### 5.1.1 Stratégie d'Implémentation des Règles

**Principe Architectural Fondamental** : Le moteur de calcul liturgique doit être **strictement découplé** de la source des règles liturgiques.

**Invariants Structurels** :

1. **Séparation Moteur / Source des Règles**
   - Le moteur (`SlowPath`, `TemporalLayer`, `SanctoralLayer`) ne dépend **jamais** d'une implémentation concrète des règles
   - Les règles sont toujours fournies via une **abstraction** (trait, fonction de chargement, provider)
   - L'origine des règles (hardcodées, fichiers, base de données) est transparente pour le moteur

2. **Représentation Déclarative Typée**
   - Les règles sont toujours représentées sous forme de **structures de données** fortement typées
   - Jamais de logique métier hardcodée directement dans les algorithmes de calcul
   - Les structures doivent être identiques qu'elles proviennent de code Rust ou de fichiers YAML

3. **Point d'Entrée Abstrait**
   - Un trait `RuleProvider` définit le contrat de chargement des règles
   - Plusieurs implémentations possibles : `HardcodedRuleProvider`, `YamlAotRuleProvider`, etc.
   - Le `SlowPath` accepte n'importe quelle implémentation du trait

**Architecture Cible** :

```rust
/// Trait abstrait pour la fourniture de règles liturgiques
/// 
/// Ce trait découple le moteur de calcul de la source des règles.
/// Il permet de passer d'une implémentation hardcodée à une implémentation
/// data-driven sans modifier le moteur.
pub trait RuleProvider {
    /// Retourne les règles temporelles (fêtes mobiles, déplacements)
    fn temporal_rules(&self) -> &[TemporalRule];
    
    /// Retourne les fêtes sanctorales (fêtes fixes)
    fn sanctoral_feasts(&self) -> &[SanctoralFeast];
    
    /// Retourne les règles de précédence
    fn precedence_rules(&self) -> &PrecedenceRules;
}

/// Représentation déclarative d'une règle temporelle
/// 
/// Cette structure est identique qu'elle soit créée :
/// - directement en Rust (v1.0)
/// - via un pipeline AOT depuis YAML (v2.0+)
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TemporalRule {
    pub id: u32,
    pub name: String,
    pub rule_type: TemporalRuleType,
    pub precedence: Precedence,
    pub nature: Nature,
    pub color: Color,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum TemporalRuleType {
    /// Fête relative à Pâques (ex: Ascension = Pâques + 39j)
    RelativeToEaster { offset_days: i16 },
    
    /// Règle de déplacement (ex: St-Joseph si conflit avec Semaine Sainte)
    Displacement {
        base_date: (u8, u8),  // (mois, jour)
        displaced_if: Vec<DisplacementCondition>,
        displaced_to: DisplacementTarget,
    },
    
    /// Fête mobile avec calcul custom
    CustomComputation { 
        compute_fn_id: u32,  // Référence à une fonction enregistrée
    },
}

/// Le moteur accepte n'importe quelle implémentation de RuleProvider
impl SlowPath {
    pub fn new<P: RuleProvider>(provider: P) -> Self {
        Self {
            temporal: TemporalLayer::from_rules(provider.temporal_rules()),
            sanctoral: SanctoralLayer::from_feasts(provider.sanctoral_feasts()),
            precedence: PrecedenceResolver::from_rules(provider.precedence_rules()),
        }
    }
}
```

**Stratégie Évolutive** :

**Phase 1 (v0.1 - v1.0) : Implémentation Hardcodée**

Pour limiter la complexité initiale et permettre une preuve de concept rapide, les ~50 règles liturgiques seront implémentées directement en Rust :

```rust
/// Implémentation v1.0 : Règles hardcodées
pub struct HardcodedRuleProvider {
    temporal: Vec<TemporalRule>,
    sanctoral: Vec<SanctoralFeast>,
    precedence: PrecedenceRules,
}

impl HardcodedRuleProvider {
    pub fn new_roman_rite_ordinary() -> Self {
        Self {
            temporal: vec![
                TemporalRule {
                    id: 0x00001,
                    name: "Ascension".to_string(),
                    rule_type: TemporalRuleType::RelativeToEaster { 
                        offset_days: 39 
                    },
                    precedence: Precedence::SollemnitatesFixaeMaior,
                    nature: Nature::Solemnitas,
                    color: Color::Albus,
                },
                TemporalRule {
                    id: 0x00002,
                    name: "Pentecôte".to_string(),
                    rule_type: TemporalRuleType::RelativeToEaster { 
                        offset_days: 49 
                    },
                    precedence: Precedence::SollemnitatesFixaeMaior,
                    nature: Nature::Solemnitas,
                    color: Color::Rubeus,
                },
                // ... ~50 règles au total
            ],
            sanctoral: vec![
                SanctoralFeast {
                    date: (1, 1),
                    name: "Sainte Marie, Mère de Dieu".to_string(),
                    precedence: Precedence::SollemnitatesGenerales,
                    nature: Nature::Solemnitas,
                    color: Color::Albus,
                    scope: FeastScope::Universal,
                },
                // ... sanctoral complet
            ],
            precedence: PrecedenceRules::default_roman(),
        }
    }
}

impl RuleProvider for HardcodedRuleProvider {
    fn temporal_rules(&self) -> &[TemporalRule] {
        &self.temporal
    }
    
    fn sanctoral_feasts(&self) -> &[SanctoralFeast] {
        &self.sanctoral
    }
    
    fn precedence_rules(&self) -> &PrecedenceRules {
        &self.precedence
    }
}

// Usage
let rules = HardcodedRuleProvider::new_roman_rite_ordinary();
let slow_path = SlowPath::new(rules);
```

**Avantages Phase 1** :
- Démarrage immédiat (pas de parser YAML)
- Type-safety garantie par le compilateur
- Refactoring facile (IDE assist)
- Debugging simple

**Phase 2 (v2.0+) : Pipeline AOT Data-Driven**

Une fois l'architecture stabilisée, introduction d'un pipeline AOT pour externaliser les règles :

```rust
/// Implémentation v2.0+ : Règles depuis YAML compilées en AOT
/// 
/// Le pipeline AOT (liturgical-calendar-forge):
/// 1. Lit les fichiers YAML (roman-rite-ordinary.yaml)
/// 2. Valide la cohérence des règles
/// 3. Génère du code Rust (rules_generated.rs)
/// 4. Compile dans le binaire
pub struct YamlAotRuleProvider {
    temporal: &'static [TemporalRule],
    sanctoral: &'static [SanctoralFeast],
    precedence: &'static PrecedenceRules,
}

impl YamlAotRuleProvider {
    /// Généré automatiquement par le pipeline AOT
    /// 
    /// Ce code est produit depuis roman-rite-ordinary.yaml
    pub fn new_roman_rite_ordinary() -> Self {
        // Ces structures sont générées à la compilation (macro ou build.rs)
        Self {
            temporal: &TEMPORAL_RULES_GENERATED,
            sanctoral: &SANCTORAL_FEASTS_GENERATED,
            precedence: &PRECEDENCE_RULES_GENERATED,
        }
    }
}

impl RuleProvider for YamlAotRuleProvider {
    fn temporal_rules(&self) -> &[TemporalRule] {
        self.temporal
    }
    
    fn sanctoral_feasts(&self) -> &[SanctoralFeast] {
        self.sanctoral
    }
    
    fn precedence_rules(&self) -> &PrecedenceRules {
        self.precedence
    }
}
```

**Pipeline AOT** :

```yaml
# config/roman-rite-ordinary.yaml
temporal_rules:
  - id: 0x00001
    name: "Ascension"
    type: relative_to_easter
    offset_days: 39
    precedence: 1
    nature: solemnitas
    color: albus
    
  - id: 0x00002
    name: "Pentecôte"
    type: relative_to_easter
    offset_days: 49
    precedence: 1
    nature: solemnitas
    color: rubeus

sanctoral_feasts:
  - date: [1, 1]
    name: "Sainte Marie, Mère de Dieu"
    precedence: 4
    nature: solemnitas
    color: albus
    scope: universal
```

```bash
# Compilation AOT (intégrée dans liturgical-calendar-forge)
$ liturgical-calendar-forge compile-rules \
    --input config/roman-rite-ordinary.yaml \
    --output src/rules_generated.rs
    
# Validation stricte
✓ 50 règles temporelles validées
✓ 365 fêtes sanctorales validées
✓ Aucune collision de FeastID
✓ Précédence cohérente
→ Code Rust généré : src/rules_generated.rs (10,234 lignes)
```

**Avantages Phase 2** :
- Externalisation des règles (contribution communautaire facile)
- Validation stricte à la compilation (pas d'erreur runtime)
- Performance identique (structures statiques)
- Multilingue simplifié (un YAML par rite)

**Garanties de Migration** :

La transition Phase 1 → Phase 2 ne nécessite **aucune modification** du moteur :

```rust
// v1.0 : Hardcodé
let rules = HardcodedRuleProvider::new_roman_rite_ordinary();
let slow_path = SlowPath::new(rules);

// v2.0 : AOT depuis YAML
let rules = YamlAotRuleProvider::new_roman_rite_ordinary();
let slow_path = SlowPath::new(rules);  // Même code !
```

Le contrat `RuleProvider` reste identique, seule l'implémentation change.

**Principe de Conception Clé** :

> *"Le moteur calcule. Les règles décrivent. L'origine des règles est un détail d'implémentation."*

Cette séparation garantit que l'investissement dans le moteur (Phase 1) n'est jamais perdu lors de l'externalisation (Phase 2).

### 5.2 Architecture Pipeline

```rust
pub struct CalendarBuilder {
    /// Configuration source (années, couches, règles)
    config: Config,

    /// Registry des FeastID (évite collisions)
    feast_registry: FeastRegistry,

    /// Slow Path pour génération
    slow_path: SlowPath,

    /// Cache des jours calculés.
    /// IMPORTANT : BTreeMap pour déterminisme (ordre de sérialisation garanti).
    /// Type : DayPacked — cohérent avec le Data Body du .kald (u32 par entrée).
    /// La Forge calcule via Day (SlowPath) puis convertit immédiatement en DayPacked.
    /// Conforme roadmap §2.2 (correction B4).
    cache: BTreeMap<(i16, u16), DayPacked>,
}

impl CalendarBuilder {
    pub fn new(config: Config) -> Result<Self, RuntimeError> {
        let feast_registry = FeastRegistry::load(&config.registry_path)?;
        let slow_path = SlowPath::from_config(&config)?;

        Ok(Self {
            config,
            feast_registry,
            slow_path,
            cache: BTreeMap::new(),
        })
    }

    /// Génère le calendrier complet.
    pub fn build(mut self) -> Result<Calendar, RuntimeError> {
        let start_year = self.config.start_year;
        let end_year = start_year + self.config.year_count as i16;

        // Validation des bornes
        // ERREUR : DomainError::YearOutOfBounds — une borne hors du domaine grégorien
        // canonique est une violation de domaine, pas une erreur d'I/O.
        // Conforme roadmap §2.2 (correction B3).
        if start_year < 1583 || end_year > 4099 {
            return Err(RuntimeError::Domain(DomainError::YearOutOfBounds(start_year)));
        }

        for year in start_year..end_year {
            let max_day = if is_leap_year(year as i32) { 366 } else { 365 };

            for day in 1..=max_day {
                // SlowPath produit Day (logique) → converti immédiatement en DayPacked
                let liturgical_day: DayPacked = self.slow_path.compute(year, day)
                    .map(DayPacked::from)
                    .map_err(RuntimeError::Domain)?;
                self.cache.insert((year, day), liturgical_day);
            }
            // Jour 366 des années non-bissextiles : absent du cache.
            // write_kald écrit 0xFFFFFFFF (DayPacked::invalid) pour les entrées manquantes.
            // get_day() retourne DayPacked::invalid() avant même de lire le fichier.
        }

        Ok(Calendar {
            start_year,
            year_count: self.config.year_count,
            data: self.cache,
        })
    }
}
```

### 5.3 Sérialisation Binaire

```rust
pub struct Calendar {
    pub start_year: i16,
    pub year_count: u16,
    /// Type : DayPacked — compact, cohérent avec le Data Body du .kald.
    /// Conforme roadmap §2.2 (correction B4/C1).
    pub data: BTreeMap<(i16, u16), DayPacked>,
}

impl Calendar {
    /// Écrit le fichier .kald (déterministe, sans UB)
    pub fn write_kald(&self, path: &str) -> Result<(), IoError> {
        let mut file = File::create(path)?;

        // Construction du header
        let header = Header {
            magic: *b"KALD",
            version: 1,
            start_year: self.start_year,
            year_count: self.year_count,
            flags: 0,  // Pas de compression/checksum pour v1
            _padding: [0, 0, 0, 0],
        };

        // Sérialisation du header (endianness native, sans UB)
        file.write_all(&header.to_bytes())?;

        // Data Body (ordre strict : années puis jours)
        for year in self.start_year..(self.start_year + self.year_count as i16) {
            for day in 1..=366 {
                let packed: u32 = self
                    .data
                    .get(&(year, day))
                    // DayPacked : extraction zero-cost via as_u32()
                    .map(|dp| dp.as_u32())
                    // Padding jour 366 pour années non-bissextiles : 0xFFFFFFFF
                    // (DayPacked::invalid — Precedence=15 hors domaine, non décodable)
                    .unwrap_or(0xFFFFFFFF_u32);
                file.write_all(&packed.to_ne_bytes())?;
            }
        }

        Ok(())
    }
}
```

### 5.4 Test de Déterminisme Cross-Platform

```yaml
# .github/workflows/determinism.yml
name: Cross-Build Determinism

on: [push, pull_request]

jobs:
  build-linux:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - run: cargo build --release --bin liturgical-calendar-forge
      - run: ./target/release/liturgical-calendar-forge build --config test.toml
      - run: sha256sum france.kald > linux-hash.txt
      - uses: actions/upload-artifact@v3
        with:
          name: linux-build
          path: |
            france.kald
            linux-hash.txt

  build-macos:
    runs-on: macos-latest
    steps:
      - uses: actions/checkout@v3
      - run: cargo build --release --bin liturgical-calendar-forge
      - run: ./target/release/liturgical-calendar-forge build --config test.toml
      - run: shasum -a 256 france.kald > macos-hash.txt
      - uses: actions/upload-artifact@v3
        with:
          name: macos-build
          path: |
            france.kald
            macos-hash.txt

  build-windows:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - run: cargo build --release --bin liturgical-calendar-forge
      - run: ./target/release/liturgical-calendar-forge.exe build --config test.toml
      - run: certutil -hashfile france.kald SHA256 > windows-hash.txt
      - uses: actions/upload-artifact@v3
        with:
          name: windows-build
          path: |
            france.kald
            windows-hash.txt

  compare:
    needs: [build-linux, build-macos, build-windows]
    runs-on: ubuntu-latest
    steps:
      - uses: actions/download-artifact@v3
      - name: Compare SHA-256
        run: |
          LINUX_HASH=$(cat linux-build/linux-hash.txt | awk '{print $1}')
          MACOS_HASH=$(cat macos-build/macos-hash.txt | awk '{print $1}')
          WINDOWS_HASH=$(cat windows-build/windows-hash.txt | awk '{print $1}')

          if [ "$LINUX_HASH" = "$MACOS_HASH" ] && [ "$MACOS_HASH" = "$WINDOWS_HASH" ]; then
            echo "✓ Determinism verified across platforms"
            exit 0
          else
            echo "✗ Hash mismatch detected"
            echo "Linux:   $LINUX_HASH"
            echo "macOS:   $MACOS_HASH"
            echo "Windows: $WINDOWS_HASH"
            exit 1
          fi
```

---

## 6. Format Strings (.lits)

### 6.1 Structure Multi-Langue

**Layout Fichier** :

```
[Header - 12 octets]
magic: [u8; 4]      // "LITS"
version: u16        // 1
lang_code: [u8; 2]  // "fr", "en", "la"
entry_count: u32    // Nombre d'entrées

[Index Section - N × 12 octets]
struct IndexEntry {
    feast_id: u32,         // FeastID correspondant
    offset: u32,           // Offset vers le texte (depuis début Data Section)
    length: u32,           // Longueur du texte UTF-8
}

[Data Section - Textes UTF-8]
// Strings encodés en UTF-8, concaténés séquentiellement
```

### 6.2 Provider de Strings

```rust
use memmap2::Mmap;
use std::collections::HashMap;

pub struct StringProvider {
    /// Memory-mapped .lits
    mmap: Mmap,

    /// Index FeastID → (offset, length)
    index: HashMap<u32, (usize, usize)>,
}

impl StringProvider {
    pub fn load(path: &str) -> Result<Self, IoError> {
        let file = File::open(path)?;
        let mmap = unsafe { Mmap::map(&file)? };

        // Validation header
        let magic = &mmap[0..4];
        if magic != b"LITS" {
            return Err(IoError::InvalidMagic(*b"LITS"));
        }

        let entry_count = u32::from_ne_bytes([mmap[8], mmap[9], mmap[10], mmap[11]]) as usize;

        // Construction de l'index
        let mut index = HashMap::with_capacity(entry_count);
        let index_start = 12;

        for i in 0..entry_count {
            let entry_offset = index_start + (i * 12);
            let feast_id = u32::from_ne_bytes([
                mmap[entry_offset],
                mmap[entry_offset + 1],
                mmap[entry_offset + 2],
                mmap[entry_offset + 3],
            ]);

            let offset = u32::from_ne_bytes([
                mmap[entry_offset + 4],
                mmap[entry_offset + 5],
                mmap[entry_offset + 6],
                mmap[entry_offset + 7],
            ]) as usize;

            let length = u32::from_ne_bytes([
                mmap[entry_offset + 8],
                mmap[entry_offset + 9],
                mmap[entry_offset + 10],
                mmap[entry_offset + 11],
            ]) as usize;

            index.insert(feast_id, (offset, length));
        }

        Ok(Self { mmap, index })
    }

    /// Récupère le nom d'une fête (zero-copy)
    pub fn get_feast_name(&self, feast_id: u32) -> Option<&str> {
        self.index.get(&feast_id).and_then(|(offset, length)| {
            let data_section_start = 12 + (self.index.len() * 12);
            let start = data_section_start + offset;
            let end = start + length;

            std::str::from_utf8(&self.mmap[start..end]).ok()
        })
    }
}
```

---

## 7. Runtime Provider

### 7.1 Architecture Fast-Slow Path

**Principe de Sélection** :

Le Provider maintient deux chemins de calcul **fonctionnellement équivalents** :

1. **Fast Path** : Lecture directe dans le fichier `.kald` si l'année est dans la fenêtre optimisée
2. **Slow Path** : Calcul algorithmique pour toutes les années [1583, 4099]

La sélection est une **optimisation de performance**, pas une correction d'erreur. Les deux chemins produisent des résultats identiques (validé par tests d'identité).

```rust
use memmap2::{Mmap, MmapOptions};
use std::fs::File;

pub struct Provider {
    /// Fast Path : données mmap (fenêtre optimisée)
    /// None = système fonctionne en Slow Path uniquement
    fast_path: Option<FastPath>,

    /// Slow Path : calcul algorithmique (système complet)
    /// Toujours présent, couvre 1583-4099
    slow_path: SlowPath,

    /// Fenêtre couverte par Fast Path (si présent)
    range: (i16, u16),  // (start_year, year_count) — u16 natif, évite le cast silencieux

    /// Provider de strings localisés
    string_provider: StringProvider,

    /// Télémétrie
    telemetry: Telemetry,
}

struct FastPath {
    /// Memory-mapped .kald
    mmap: Mmap,

    /// Slice vers le Data Body (référence statique validée)
    data: &'static [u32],

    /// Année de départ du fichier
    start_year: i16,
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
    /// Charge le Provider avec fenêtre Fast Path
    ///
    /// Exemple :
    /// ```rust
    /// let rules = HardcodedRuleProvider::new_roman_rite_ordinary();
    /// let slow_path = SlowPath::new(rules);
    /// let provider = Provider::new("france.kald", "france.lits", slow_path)?;
    /// ```
    pub fn new(data_path: &str, lang_path: &str, slow_path: SlowPath) -> Result<Self, RuntimeError> {
        Self::new_with_strategy(data_path, lang_path, LoadStrategy::Preload, slow_path)
    }
    
    /// Crée un Provider sans Fast Path (Slow Path uniquement)
    /// 
    /// Utile pour :
    /// - Recherche historique (années < 1900)
    /// - Systèmes contraints en mémoire
    /// - Calculs ponctuels sans optimisation
    pub fn slow_only() -> Self {
        // SlowPath::new() requiert un RuleProvider (voir section 5.1.1).
        // En production, fournir HardcodedRuleProvider::new_roman_rite_ordinary().
        // Cette méthode est un placeholder — l'appelant doit construire le SlowPath
        // via Provider::with_slow_path(slow_path) ou une factory dédiée.
        unimplemented!(
            "slow_only() requiert un RuleProvider. \
             Utiliser Provider::from_slow_path(SlowPath::new(rules))."
        )
    }

    /// Charge avec stratégie mémoire explicite
    ///
    /// Le SlowPath est passé en argument car il requiert un RuleProvider
    /// (voir section 5.1.1). L'appelant construit le SlowPath via :
    ///   `SlowPath::new(HardcodedRuleProvider::new_roman_rite_ordinary())`
    pub fn new_with_strategy(
        data_path: &str,
        lang_path: &str,
        strategy: LoadStrategy,
        slow_path: SlowPath,
    ) -> Result<Self, RuntimeError> {
        // Chargement du fichier .kald
        let file = File::open(data_path)?;
        let mmap = unsafe { MmapOptions::new().map(&file)? };

        // Application de la stratégie
        match strategy {
            LoadStrategy::Lazy => {
                // Rien à faire, accès à la demande (page faults possibles)
            }
            LoadStrategy::Preload => {
                // Hint au kernel pour prefetch
                unsafe {
                    libc::madvise(
                        mmap.as_ptr() as *mut _,
                        mmap.len(),
                        libc::MADV_WILLNEED,
                    );
                }
            }
            LoadStrategy::Locked => {
                // Lock en RAM pour hard real-time (évite page faults)
                unsafe {
                    libc::mlock(
                        mmap.as_ptr() as *const _,
                        mmap.len(),
                    );
                }
            }
        }

        // Validation et construction
        // &mmap[..] : extrait un &[u8] depuis le Mmap — conforme à la signature pub fn validate_header(bytes: &[u8])
        let header = validate_header(&mmap[..])?;
        let data_body = parse_data_body(&mmap, &header)?;
        let string_provider = StringProvider::load(lang_path)?;

        Ok(Self {
            fast_path: Some(FastPath {
                mmap,
                data: data_body,
                start_year: header.start_year
            }),
            slow_path,
            range: (header.start_year, header.year_count),
            string_provider,
            telemetry: Telemetry::default(),
        })
    }

    /// Récupère un jour liturgique
    /// 
    /// Sélection automatique Fast/Slow selon la fenêtre optimisée :
    /// - Si année dans [range.0, range.0+range.1) ET Fast Path disponible → Fast Path
    /// - Sinon → Slow Path
    /// 
    /// Les deux chemins sont fonctionnellement équivalents.
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

        // Tentative Fast Path (fenêtre optimisée)
        if let Some(ref fast) = self.fast_path {
            if year >= self.range.0 && year < self.range.0 + self.range.1 as i16 {
                self.telemetry.fast_path_hits.fetch_add(1, Ordering::Relaxed);

                let idx = index_day(year, day_of_year, fast.start_year);
                let packed = fast.data[idx];

                match DayPacked::try_from_u32(packed) {
                    Ok(day) => return day,
                    Err(_) => {
                        // Corruption détectée
                        self.telemetry.corrupted_entries.fetch_add(1, Ordering::Relaxed);
                        self.log_corruption(year, day_of_year, packed);
                        return DayPacked::invalid();
                    }
                }
            }
        }

        // Slow Path (calcul algorithmique complet)
        if year >= 1583 && year <= 4099 {
            self.telemetry.slow_path_hits.fetch_add(1, Ordering::Relaxed);
            return self.slow_path.compute(year, day_of_year)
                .map(|logic| DayPacked::from(logic))
                .unwrap_or_else(|| DayPacked::invalid());
        }

        // Hors limites calendrier grégorien canonique
        self.telemetry.out_of_bounds_queries.fetch_add(1, Ordering::Relaxed);
        DayPacked::invalid()
    }

    /// Récupère les métriques de télémétrie
    pub fn get_telemetry(&self) -> TelemetrySnapshot {
        TelemetrySnapshot {
            fast_path_hits: self.telemetry.fast_path_hits.load(Ordering::Relaxed),
            slow_path_hits: self.telemetry.slow_path_hits.load(Ordering::Relaxed),
            invalid_returns: self.telemetry.invalid_returns.load(Ordering::Relaxed),
            corrupted_entries: self.telemetry.corrupted_entries.load(Ordering::Relaxed),
            out_of_bounds_queries: self.telemetry.out_of_bounds_queries.load(Ordering::Relaxed),
        }
    }

    /// Calcul direct via Slow Path (pour tests d'identité Fast vs Slow)
    ///
    /// Exposé publiquement pour les suites de tests uniquement.
    /// Ne doit pas être utilisé dans le code de production (préférer get_day).
    pub fn compute_slow(&self, year: i16, day_of_year: u16) -> Result<Day, DomainError> {
        self.slow_path.compute(year, day_of_year)
    }

    /// Log structuré de corruption
    fn log_corruption(&self, year: i16, day_of_year: u16, packed: u32) {
        // Reconstruction du CorruptionInfo via try_from_u32
        let (invalid_field, invalid_value) = match DayPacked::try_from_u32(packed) {
            Ok(_) => ("none", 0u8),  // Ne devrait pas arriver ici
            Err(info) => (info.invalid_field, info.invalid_value),
        };

        // year et day_of_year sont des paramètres locaux — CorruptionInfo ne les contient pas.
        // L'offset est calculé ici, au point d'appel où range.0 est disponible.
        eprintln!(
            "CORRUPTION: year={}, day={}, packed=0x{:08X}, field={}, value={}, offset={}",
            year,
            day_of_year,
            packed,
            invalid_field,
            invalid_value,
            index_day(year, day_of_year, self.range.0),
        );
    }
}

/// Calcule l'index dans le Data Body
#[inline(always)]
fn index_day(year: i16, day_of_year: u16, start_year: i16) -> usize {
    let year_offset = (year - start_year) as usize;
    (year_offset * 366) + (day_of_year as usize - 1)
}

pub enum LoadStrategy {
    /// Lazy loading (page faults possibles)
    Lazy,

    /// Preload avec madvise WILLNEED
    Preload,

    /// Lock en RAM (hard real-time)
    Locked,
}
```

### 7.2 Sécurité Mémoire (Invariant Lifetime Documenté - Audit #5)

#### Pourquoi `&'static [u32]` et non un accès par méthode

**Problème structurel : la self-referential struct**

La forme naïve serait de stocker directement la slice dans `FastPath` :

```rust
// ❌ REFUSÉ PAR RUSTC — self-referential struct
struct FastPath {
    mmap: Mmap,
    data: &[u32],  // référence vers mmap, qui est dans la même struct
}
```

Rust refuse cette construction : `data` est une référence vers des données
*possédées* par la même struct (`mmap`). Il est impossible d'annoter le
lifetime de `data` sans faire référence à `FastPath` elle-même, ce que Rust
interdit formellement. Le compilateur produira une erreur de lifetime à
ce stade, sans solution directe en Rust safe.

**Deux solutions idiomatiques :**

1. **Accès par méthode** (zero-cost, pas d'`unsafe`) :
   ```rust
   impl FastPath {
       fn data(&self) -> &[u32] {
           // recalcul du pointeur à chaque appel — inliné par le compilateur
           let bytes = &self.mmap[16..];
           bytemuck::cast_slice(bytes)  // ou slice::from_raw_parts en unsafe
       }
   }
   ```
   Avantage : Rust safe, aucun lifetime artificiel.
   Inconvénient : la spec requiert un accès indexé direct `data[idx]` depuis
   `get_day` — cette forme impose un appel de méthode intermédiaire.

2. **`&'static [u32]`** (choix retenu) : étendre le lifetime à `'static`
   via `unsafe`, en garantissant par invariant que le `Mmap` vit aussi
   longtemps que la référence.

   Le compilateur **accepte** `&'static [u32]` comme champ de struct sans
   restriction de lifetime sur `FastPath`. C'est le contrat `unsafe` que
   `parse_data_body` établit explicitement.

**Comportement compilateur attendu :**

Si vous tentez d'annoter un lifetime non-`'static` sur ce champ
(ex: `data: &'a [u32]`), le compilateur demandera un paramètre de lifetime
sur `FastPath<'a>`, puis sur `Provider<'a>`, et enfin sur toute fonction
qui les construit — cascade qui rend l'API publique inutilisable.
Le choix `'static` + `unsafe` coupe cette cascade.

```rust
/// Parse le Data Body et retourne une slice &'static [u32]
///
/// INVARIANT CRITIQUE (Audit #5) :
/// La référence &'static retournée est valide TANT QUE le Mmap vit.
/// Le Mmap est stocké dans FastPath et doit vivre au moins aussi longtemps
/// que toute référence extraite.
///
/// RÈGLES DE SÉCURITÉ :
/// 1. Ne jamais remap/reload le Mmap sans détruire le Provider
/// 2. Ne jamais extraire la slice hors du contexte du Provider
/// 3. Le FastPath possède le Mmap (ownership), garantissant le lifetime
fn parse_data_body(mmap: &Mmap, header: &Header) -> Result<&'static [u32], IoError> {
    let expected_size = 16 + (header.year_count as usize * 1464);

    if mmap.len() != expected_size {
        return Err(IoError::CorruptedFile {
            expected: expected_size,
            actual: mmap.len(),
        });
    }

    // Vérification alignement u32
    let data_ptr = unsafe { mmap.as_ptr().add(16) };
    if (data_ptr as usize) % 4 != 0 {
        return Err(IoError::MisalignedData);
    }

    // Conversion sécurisée
    // SAFETY :
    // - Alignement vérifié (% 4 == 0)
    // - Taille validée (len == expected_size)
    // - Lifetime 'static justifié par ownership du Mmap dans FastPath
    let len = header.year_count as usize * 366;
    let slice = unsafe {
        std::slice::from_raw_parts(data_ptr as *const u32, len)
    };

    Ok(slice)
}
```

---

## 8. Versioning Binaire et Migration

### 8.1 Politique de Versioning

**Stratégie de Migration** :

```rust
pub enum MigrationStrategy {
    /// Refuser le chargement si version différente
    Strict,

    /// Tenter la migration automatique (v1 → v2)
    AutoMigrate,

    /// Charger en mode dégradé (features v2 ignorées)
    BestEffort,
}

impl Provider {
    pub fn load_with_migration(
        path: &str,
        strategy: MigrationStrategy,
    ) -> Result<Self, RuntimeError> {
        let header = validate_header_versioned(path)?;

        match (header.version, strategy) {
            (1, _) => {
                // Version actuelle, chargement normal
                Self::load_v1(path)
            }
            (2, MigrationStrategy::AutoMigrate) => {
                // Migration automatique
                Self::migrate_v1_to_v2_and_load(path)
            }
            (2, MigrationStrategy::BestEffort) => {
                // Charge v2 en ignorant features v2
                Self::load_v2_best_effort(path)
            }
            (v, MigrationStrategy::Strict) => {
                Err(RuntimeError::Io(IoError::UnsupportedVersion(v)))
            }
        }
    }
}
```

### 8.2 Utilitaire d'Inspection

```bash
# Utilitaire CLI pour diagnostics
$ kald-inspect france.kald

Format: KALD v1
Start Year: 2025
Year Count: 300
File Size: 439,216 bytes (expected: 439,216 ✓)
Checksum: None
Compression: None
Flags: 0x0000

First 10 entries:
  2025-001: 0x40100042 (Prec=4/SollemnitatesGenerales, Nat=Solemnitas, Albus, TempusNativitatis, #0x00042)
  2025-002: 0xA6100000 (Prec=10/FeriaeAdventusEtOctavaNativitatis, Nat=Feria, Albus, TempusNativitatis, #0x00000)
  ...

Validation: ✓ All entries decodable
Corruption: 0 invalid entries detected

Compatibility:
  ✓ Can be loaded by liturgical-calendar-runtime v1.x
  ✓ Can be migrated to v2 format
```

**Implémentation** :

```rust
// kald-inspect/src/main.rs
fn inspect_file(path: &str) -> Result<Report, IoError> {
    let file = File::open(path)?;
    let mmap = unsafe { Mmap::map(&file)? };

    let header = validate_header(&mmap[..])?;

    let mut report = Report {
        version: header.version,
        start_year: header.start_year,
        year_count: header.year_count,
        file_size: mmap.len(),
        expected_size: 16 + (header.year_count as usize * 1464),
        corruption_count: 0,
        entries: Vec::new(),
    };

    // Scan de tous les u32 pour détecter corruptions
    let data = parse_data_body(&mmap, &header)?;
    for (i, &packed) in data.iter().enumerate() {
        match Day::try_from_u32(packed) {
            Ok(day) => {
                if i < 10 {
                    report.entries.push(day);
                }
            }
            Err(_) => {
                report.corruption_count += 1;
                eprintln!("Corruption at offset {}: 0x{:08X}", i, packed);
            }
        }
    }

    Ok(report)
}
```

---

## 9. Gestion des Corruptions et Diagnostics

### 9.1 Hiérarchie d'Erreurs par Couche

**Principe** : chaque crate expose son propre type d'erreur. Les couches supérieures agrègent via `From`. Zéro couplage entre `core` et l'infrastructure I/O.

```
DomainError      ← liturgical-calendar-core   (validation bitfields, bornes)
IoError          ← liturgical-calendar-io     (format .kald, I/O fichier)
RegistryError    ← liturgical-calendar-forge  (FeastID allocation/collision)
RuntimeError     ← liturgical-calendar-runtime (agrège tout + corruption)
```

```rust
// ─────────────────────────────────────────────
// liturgical-calendar-core/src/error.rs
// ─────────────────────────────────────────────
//
// INVARIANT no_std : ce fichier ne peut importer que core::*.
// Pas de std::error::Error, pas de alloc, pas de String.
// Le crate racine déclare : #![no_std]
// Les crates consommateurs (io, forge, runtime) ont accès à std.

/// Erreurs du domaine liturgique pur.
///
/// Produites par : Season::try_from_u8, Color::try_from_u8,
/// Precedence::try_from_u8, Nature::try_from_u8, SlowPath::compute,
/// SeasonBoundaries::compute, Day::try_from_u32.
///
/// GARANTIES no_std :
/// - Aucun variant ne contient String ni Box<dyn _>
/// - #[derive(Debug)] utilise core::fmt::Debug (disponible no_std)
/// - impl Display utilise core::fmt::Display (disponible no_std)
/// - Pas d'impl std::error::Error (std uniquement)
///   → les crates std peuvent l'intégrer via RuntimeError qui, lui, est std
#[derive(Copy, Clone, Debug, PartialEq, Eq)]
pub enum DomainError {
    InvalidSeason(u8),
    InvalidColor(u8),
    InvalidPrecedence(u8),
    InvalidNature(u8),
    ReservedBitSet,
    YearOutOfBounds(i16),
}

impl core::fmt::Display for DomainError {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        match self {
            Self::InvalidSeason(v)    => write!(f, "invalid season: {}", v),
            Self::InvalidColor(v)     => write!(f, "invalid color: {}", v),
            Self::InvalidPrecedence(v) => write!(f, "invalid precedence: {}", v),
            Self::InvalidNature(v)    => write!(f, "invalid nature: {}", v),
            Self::ReservedBitSet      => write!(f, "reserved bit [18] must be 0"),
            Self::YearOutOfBounds(y)  => write!(f, "year out of bounds: {}", y),
        }
    }
}

// impl std::error::Error est DÉLIBÉRÉMENT absent du crate core.
// Il est fourni par le crate runtime (std) via le wrapper RuntimeError :
//
//   #[cfg(feature = "std")]
//   impl std::error::Error for RuntimeError {}
//
// Cela préserve la compilabilité no_std de core tout en exposant
// l'interface std::error::Error aux consommateurs qui en ont besoin.

impl DomainError {
    /// Nom du champ invalide — utilisé par DayPacked::try_from_u32
    /// pour construire un CorruptionInfo sans allocation.
    pub fn field_name(&self) -> &'static str {
        match self {
            Self::InvalidSeason(_)    => "season",
            Self::InvalidColor(_)     => "color",
            Self::InvalidPrecedence(_) => "precedence",
            Self::InvalidNature(_)    => "nature",
            Self::ReservedBitSet      => "reserved",
            Self::YearOutOfBounds(_)  => "year",
        }
    }

    /// Valeur numérique hors domaine (0 pour YearOutOfBounds et ReservedBitSet).
    pub fn field_value(&self) -> u8 {
        match self {
            Self::InvalidSeason(v) | Self::InvalidColor(v)
            | Self::InvalidPrecedence(v) | Self::InvalidNature(v) => *v,
            Self::ReservedBitSet | Self::YearOutOfBounds(_) => 0,
        }
    }
}

// ─────────────────────────────────────────────
// liturgical-calendar-io/src/error.rs
// ─────────────────────────────────────────────

/// Erreurs de format et d'I/O fichier.
///
/// Produites par : validate_header, parse_data_body,
/// Calendar::write_kald, StringProvider::load.
#[derive(Debug)]
pub enum IoError {
    Io(std::io::Error),
    FileTooSmall,
    InvalidMagic([u8; 4]),
    UnsupportedVersion(u16),
    UnsupportedFlags { found: u16, known: u16, unknown_bits: u16 },
    InvalidPadding([u8; 4]),
    // NOTE : InvalidYearRange supprimée — les bornes hors domaine grégorien sont
    // désormais routées vers DomainError::YearOutOfBounds (correction C2/B3).
    // Une borne invalide est une violation de domaine, pas une erreur d'I/O.
    InvalidYearCount(u16),
    MisalignedData,
    CorruptedFile { expected: usize, actual: usize },
}

impl From<std::io::Error> for IoError {
    fn from(e: std::io::Error) -> Self { Self::Io(e) }
}

// ─────────────────────────────────────────────
// liturgical-calendar-forge/src/error.rs
// ─────────────────────────────────────────────

/// Erreurs d'allocation et d'interopérabilité du FeastID Registry.
///
/// Produites par : FeastRegistry::allocate_next, register, import,
/// et les fonctions de normalisation de config (normalize_color, normalize_nature).
#[derive(Debug)]
pub enum RegistryError {
    /// Deux forges ont alloué le même FeastID avec des noms différents.
    FeastIDCollision(u32),
    /// L'espace séquentiel 12 bits d'un scope/category est épuisé (max 4096 entrées).
    FeastIDExhausted { scope: u8, category: u8 },
    /// Scope > 3 ou category > 15.
    InvalidScopeCategory { scope: u8, category: u8 },
    /// Chaîne de couleur inconnue dans un fichier de configuration TOML/YAML.
    /// Contient la valeur originale pour le message d'erreur à l'opérateur.
    UnknownColorString(String),
    /// Chaîne de nature inconnue dans un fichier de configuration TOML/YAML.
    UnknownNatureString(String),
    /// Valeur de précédence hors domaine (0–12) dans un fichier de configuration.
    InvalidPrecedenceValue(u8),
    // NOTE : import() retourne Ok(ImportReport) même en présence de collisions.
    // Les collisions de noms sont rapportées via ImportReport::collisions, non via Err.
    // Voir §3.3 pour le comportement canonique.
}

// ─────────────────────────────────────────────
// liturgical-calendar-runtime/src/error.rs
// ─────────────────────────────────────────────

/// Erreur applicative du runtime — agrège toutes les couches inférieures.
///
/// C'est le type exposé aux consommateurs de la bibliothèque et au FFI.
/// Le mapping From garantit la propagation sans perte d'information.
#[derive(Debug)]
pub enum RuntimeError {
    /// Erreur de domaine liturgique (validation bitfields).
    Domain(DomainError),
    /// Erreur de format ou d'I/O fichier.
    Io(IoError),
    /// Entrée corrompue détectée dans le Data Body.
    CorruptedEntry(CorruptionInfo),
    /// Multiples corruptions détectées (scan complet).
    MultipleCorruptions(Vec<CorruptionInfo>),
}

impl From<DomainError> for RuntimeError {
    fn from(e: DomainError) -> Self { Self::Domain(e) }
}

impl From<IoError> for RuntimeError {
    fn from(e: IoError) -> Self { Self::Io(e) }
}

impl From<std::io::Error> for RuntimeError {
    fn from(e: std::io::Error) -> Self { Self::Io(IoError::Io(e)) }
}
```

**Règle de mapping crate ↔ type** :

| Crate | Type d'erreur exposé | Consomme |
|-------|---------------------|----------|
| `core` | `DomainError` | — |
| `io` | `IoError` | `std::io::Error` |
| `forge` | `RegistryError`, `IoError` | `DomainError` via `?` |
| `runtime` | `RuntimeError` | `DomainError`, `IoError` |

**Note** : `HeaderError` (défini §2.1) reste le type interne de `validate_header`. Il est converti en `IoError` par la couche `io` avant d'être exposé.

#### Conversions `From` requises — tableau exhaustif

**Contexte compilateur** : l'opérateur `?` propage automatiquement une erreur
en appelant `From::from(e)`. Si la conversion `From<X> for Y` n'est pas
implémentée, `?` produit une erreur de compilation au niveau de l'appel,
pas au niveau de la définition du type. Ces erreurs apparaissent tard dans
le développement, lorsque les crates sont assemblées.

Le tableau suivant liste toutes les implémentations `From` qui **doivent
exister** pour que `?` fonctionne à travers les frontières de crates. Toute
conversion manquante provoquera une erreur `the trait From<X> is not
implemented for Y` à la compilation.

| `From` (source) | `For` (cible) | Crate d'implémentation | Usage |
|---|---|---|---|
| `std::io::Error` | `IoError` | `liturgical-calendar-io` | `File::open()?`, `file.write_all()?` |
| `std::io::Error` | `RuntimeError` | `liturgical-calendar-runtime` | `File::open()?` dans le Provider |
| `IoError` | `RuntimeError` | `liturgical-calendar-runtime` | propagation depuis les fonctions io |
| `DomainError` | `RuntimeError` | `liturgical-calendar-runtime` | `slow_path.compute()?` dans le runtime |
| `HeaderError` | `IoError` | `liturgical-calendar-io` | `validate_header()?` interne |
| `serde_json::Error` | `IoError` | `liturgical-calendar-io` | `serde_json::from_reader()?` dans FeastRegistry |

**Conversions absentes volontairement** :

- `DomainError → IoError` : non implémenté. Une erreur de domaine n'est pas
  une erreur I/O. Les sites qui produisent les deux utilisent `RuntimeError`
  comme type de retour commun.
- `RegistryError → RuntimeError` : non implémenté. La Forge et le Runtime
  sont des binaires distincts. `RegistryError` ne traverse pas cette frontière.

**Ordre d'implémentation recommandé** : implémenter dans l'ordre du tableau,
du plus bas (`std::io::Error → IoError`) vers le plus haut
(`DomainError → RuntimeError`). Le compilateur signalera les manques dans
cet ordre lors de la construction du workspace complet (`cargo build --workspace`).

// CorruptionInfo est défini canoniquement en section 1.1.
// Rappel : { packed_value: u32, invalid_field: &'static str,
//             invalid_value: u8, offset: Option<usize> }

### 9.2 Télémétrie Structurée

```rust
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
        if total == 0 {
            0.0
        } else {
            (self.fast_path_hits as f64) / (total as f64)
        }
    }

    pub fn corruption_rate(&self) -> f64 {
        let total = self.fast_path_hits + self.slow_path_hits;
        if total == 0 {
            0.0
        } else {
            (self.corrupted_entries as f64) / (total as f64)
        }
    }
}
```

---

## 10. Bindings FFI (C/C++ Interop)

### 10.1 API C

```c
// kal.h
// Note: The "kal_" prefix derives from "Kalendarium", the compiled annual artifact
//       used to keep function names concise while avoiding namespace 
//       collisions in C.

#ifndef KAL_H
#define KAL_H

#include <stdint.h>
#include <stdbool.h>

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

// Lifecycle
KalProvider* kal_new(const char* data_path, const char* lang_path);
void kal_free(KalProvider* handle);

// Queries
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

// Telemetry
KalTelemetry kal_get_telemetry(
    const KalProvider* handle
);

// Error handling
const char* kal_get_last_error(
    const KalProvider* handle
);

#endif // KAL_H
```

### 10.2 Implémentation Rust

**Principe général des blocs `unsafe` FFI :**

Chaque fonction FFI qui déréférence un pointeur C est déclarée `unsafe extern "C"`.
Le compilateur Rust ne peut pas vérifier les préconditions côté C — elles sont
documentées dans les sections `# Safety` ci-dessous et constituent le **contrat
que l'appelant C doit respecter**. Toute violation est un comportement indéfini (UB).

```rust
#[repr(C)]
pub struct KalResult {
    pub value: u32,
    pub error_code: u32,
}

/// Interroge un jour liturgique avec code d'erreur explicite.
///
/// # Safety
///
/// - `handle` doit être un pointeur non-nul obtenu exclusivement via `kal_new()`.
/// - `handle` ne doit pas avoir été passé à `kal_free()`.
/// - `handle` ne doit pas être accédé simultanément depuis plusieurs threads
///   (le Provider n'est pas `Sync` pour les accès en écriture de télémétrie).
/// - `handle` doit pointer vers une zone mémoire valide et alignée
///   (garanti si obtenu via `kal_new()`).
///
/// Un `handle` NULL est géré sans UB (retourne `KAL_INVALID_HANDLE`).
/// Tout autre pointeur invalide est UB non récupérable.
#[no_mangle]
pub unsafe extern "C" fn kal_get_day_checked(
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
        provider.set_last_error("Invalid day_of_year: must be 1-366");
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

/// Retourne la télémétrie courante du Provider.
///
/// # Safety
///
/// - `handle` doit être un pointeur valide obtenu via `kal_new()`, ou NULL.
/// - Un `handle` NULL retourne une `KalTelemetry` zéro sans UB.
/// - `handle` ne doit pas avoir été libéré par `kal_free()`.
#[no_mangle]
pub unsafe extern "C" fn kal_get_telemetry(
    handle: *const Provider,
) -> KalTelemetry {
    if handle.is_null() {
        return KalTelemetry::default();
    }

    let provider = unsafe { &*handle };
    let snapshot = provider.get_telemetry();

    KalTelemetry {
        fast_path_hits: snapshot.fast_path_hits,
        slow_path_hits: snapshot.slow_path_hits,
        invalid_returns: snapshot.invalid_returns,
        corrupted_entries: snapshot.corrupted_entries,
        out_of_bounds_queries: snapshot.out_of_bounds_queries,
    }
}

/// Crée un nouveau Provider et retourne un handle opaque.
///
/// # Safety
///
/// - `data_path` et `lang_path` doivent être des pointeurs vers des chaînes
///   C valides (null-terminées, encodées UTF-8).
/// - Les pointeurs `data_path` et `lang_path` doivent rester valides pour
///   toute la durée de l'appel (ils ne sont pas retenus après retour).
/// - Retourne NULL en cas d'erreur (fichier introuvable, header invalide, etc.).
/// - Le handle retourné doit être libéré par `kal_free()` exactement une fois.
///
/// # Ownership
///
/// Le handle retourné est alloué sur le heap Rust via `Box::into_raw()`.
/// Il appartient à l'appelant C jusqu'à l'appel de `kal_free()`.
#[no_mangle]
pub unsafe extern "C" fn kal_new(
    data_path: *const std::ffi::c_char,
    lang_path: *const std::ffi::c_char,
) -> *mut Provider {
    // Conversion sécurisée des chaînes C
    let data_path = match unsafe { std::ffi::CStr::from_ptr(data_path) }.to_str() {
        Ok(s) => s,
        Err(_) => return std::ptr::null_mut(),
    };
    let lang_path = match unsafe { std::ffi::CStr::from_ptr(lang_path) }.to_str() {
        Ok(s) => s,
        Err(_) => return std::ptr::null_mut(),
    };

    let rules = HardcodedRuleProvider::new_roman_rite_ordinary();
    let slow_path = SlowPath::new(rules);

    match Provider::new(data_path, lang_path, slow_path) {
        Ok(p) => Box::into_raw(Box::new(p)),
        Err(_) => std::ptr::null_mut(),
    }
}

/// Libère un handle créé par `kal_new()`.
///
/// # Safety
///
/// - `handle` doit être un pointeur obtenu via `kal_new()`, ou NULL.
/// - `handle` ne doit pas avoir déjà été passé à `kal_free()` (double-free = UB).
/// - Après appel, `handle` est invalide — ne pas le déréférencer.
/// - Un `handle` NULL est une no-op (pas d'erreur, pas d'UB).
#[no_mangle]
pub unsafe extern "C" fn kal_free(handle: *mut Provider) {
    if !handle.is_null() {
        // SAFETY : handle est non-nul, obtenu via Box::into_raw(), appelé une seule fois.
        drop(unsafe { Box::from_raw(handle) });
    }
}
```

---

/// Construit un SlowPath de test avec les règles romaines hardcodées
fn make_slow_path() -> SlowPath {
    SlowPath::new(HardcodedRuleProvider::new_roman_rite_ordinary())
}

## 11. Tests de Validation Canoniques

### 11.1 Test de Bitpacking Roundtrip

```rust
#[test]
fn test_bitpack_all_combinations() {
    use itertools::iproduct;

    for (prec, nat, color, season) in iproduct!(0..=12u8, 0..=4u8, 0..=5u8, 0..=6u8) {
        let original = Day {
            precedence: Precedence::try_from_u8(prec).unwrap(),
            nature:     Nature::try_from_u8(nat).unwrap(),
            color:      Color::try_from_u8(color).unwrap(),
            season:     Season::try_from_u8(season).unwrap(),
            feast_id:   0x12345,
        };

        let packed: u32 = original.clone().into();
        let unpacked = Day::try_from_u32(packed).unwrap();

        assert_eq!(original, unpacked);
    }
}
```

### 11.2 Test d'Identité Fast-Slow Path

```rust
#[test]
fn test_path_identity_comprehensive() {
    let provider = Provider::new("test.kald", "test.lits", make_slow_path()).unwrap();

    for year in 2025..2100 {
        let max_day: u16 = if is_leap_year(year as i32) { 366 } else { 365 };
        for day in 1..=max_day {
            let fast = provider.get_day(year, day);

            let slow = provider.compute_slow(year, day)
                .map(|logic| DayPacked::from(logic))
                .unwrap_or_else(|_| DayPacked::invalid());

            assert_eq!(
                fast.as_u32(), slow.as_u32(),
                "Divergence à {}-{:03}: fast={:08X}, slow={:08X}",
                year, day, fast.as_u32(), slow.as_u32()
            );
        }
    }
}
```

### 11.3 Test de Déterminisme de la Forge

```rust
#[test]
fn test_forge_determinism() {
    let config = Config::load("test_config.toml").unwrap();

    let output1 = CalendarBuilder::build(config.clone())
        .unwrap()
        .serialize();

    let output2 = CalendarBuilder::build(config.clone())
        .unwrap()
        .serialize();

    assert_eq!(
        output1, output2,
        "La Forge n'est pas déterministe ! Vérifier les BTreeMap."
    );
}
```

### 11.4 Test Forge → Runtime Loop (Nouveau - Audit #7)

```rust
#[test]
fn test_forge_runtime_identity() {
    // Génération
    let config = Config {
        start_year: 2025,
        year_count: 5,
        layers: vec![/* ... */],
    };

    let builder = CalendarBuilder::build(config).unwrap();
    builder.write_kald("test_loop.kald").unwrap();
    builder.write_lits("test_loop.lits", "fr").unwrap();

    // Chargement
    let provider = Provider::new("test_loop.kald", "test_loop.lits", make_slow_path()).unwrap();

    // Vérification sur 100 dates réparties
    for year in 2025..2030 {
        for day in [1, 50, 100, 150, 200, 250, 300, 365] {
            let runtime_result = provider.get_day(year, day);

            let slow_result = provider.compute_slow(year, day)
                .map(|logic| DayPacked::from(logic))
                .unwrap_or_else(|_| DayPacked::invalid());

            assert_eq!(
                runtime_result.as_u32(),
                slow_result.as_u32(),
                "Divergence Forge/Runtime pour {}-{:03}", year, day
            );
        }
    }
}
```

### 11.5 Tests de Fuzzing

```rust
// fuzz/fuzz_targets/litu_header.rs
#![no_main]
use libfuzzer_sys::fuzz_target;

fuzz_target!(|data: &[u8]| {
    if data.len() < 16 {
        return;
    }

    // Tentative de validation header
    let _ = validate_header(data);

    // Ne doit JAMAIS paniquer, même avec données aléatoires
});

// fuzz/fuzz_targets/litu_data.rs
fuzz_target!(|data: &[u8]| {
    if data.len() < 1464 + 16 {
        return;
    }

    // Création d'un fichier temporaire
    let temp = create_temp_file(data);

    // Tentative de chargement
    let _ = Provider::load(&temp);

    // Vérification : pas de panic, erreurs contrôlées
});
```

### 11.6 Tests d'Interopérabilité FeastID

```rust
#[test]
fn test_feast_id_interop() {
    // Forge 1 : France
    let mut registry_fr = FeastRegistry::load("registry_france.json").unwrap();
    let mut builder_fr = CalendarBuilder::new(2025, 10);

    // Allocation locale
    for _ in 0..100 {
        let id = registry_fr.allocate_next(2, 1).unwrap();  // National/Sanctoral
        builder_fr.add_feast_with_id(id, "Test Feast");
    }

    // Export
    export_allocations(&builder_fr, "export_france.json").unwrap();

    // Forge 2 : Allemagne
    let mut registry_de = FeastRegistry::load("registry_germany.json").unwrap();

    // Import des allocations françaises
    let result = import_allocations(&mut registry_de, "export_france.json");

    // Vérification : pas de collision
    assert!(result.is_ok(), "FeastID collision detected between France and Germany");

    // Allocation allemande après import
    for _ in 0..100 {
        let id = registry_de.allocate_next(2, 1).unwrap();
        // Les IDs ne doivent pas chevaucher les allocations françaises
        assert!(!builder_fr.has_feast_id(id));
    }
}
```

### 11.7 Tests de Télémétrie

```rust
#[test]
fn test_telemetry_corruption_tracking() {
    // Création d'un fichier avec corruption intentionnelle
    let mut data = create_valid_litu(2025, 1);

    // Injection d'un packed invalide (season = 15, hors limites)
    data[16] = 0xFF;
    data[17] = 0xFF;
    data[18] = 0xFF;
    data[19] = 0xFF;

    write_to_file("corrupted.kald", &data);

    // Chargement (doit réussir malgré corruption)
    let provider = Provider::new("corrupted.kald", "corrupted.lits", make_slow_path()).unwrap();

    // Requête sur l'entrée corrompue
    let result = provider.get_day(2025, 1);

    // Vérification : DayPacked::invalid() = 0xFFFFFFFF
    assert_eq!(result.as_u32(), 0xFFFFFFFF);

    // Vérification télémétrie
    // La corruption incrémente corrupted_entries, pas invalid_returns.
    // invalid_returns est réservé aux requêtes avec day_of_year hors [1,366].
    let telemetry = provider.get_telemetry();
    assert_eq!(telemetry.corrupted_entries, 1);
    assert_eq!(telemetry.invalid_returns, 0);
}
```

---

## 12. Annexe : Layout Hexadécimal Complet

**Fichier** : `france.kald` (2025-2324, 300 ans)

```
[Header - 16 octets]
00000000: 4B 41 4C 44 01 00 E9 07  2C 01 00 00 00 00 00 00  |KALD....,.......|
          └─────┬─────┘ │  └──┬──┘  └──┬──┘  └──────┬──────┘
             Magic    Ver  Start   Count      Flags + Padding

[Data Body — Année 2025, Jour 1 (1er janvier, Sainte Marie Mère de Dieu)]
00000010: XX XX XX XX ...
          DayPacked layout v2.0 :
            Precedence [31:28] = 4  (SollemnitatesGenerales)
            Nature     [27:25] = 0  (Solemnitas)
            Color      [24:22] = 0  (Albus)
            Season     [21:19] = 2  (TempusNativitatis)
            Reserved   [18]    = 0
            FeastID    [17:0]  = 0x00042

[Année 2025, Jour 110 (20 avril — Dominica Resurrectionis)]
000001C4: XX XX XX XX ...
          DayPacked layout v2.0 :
            Precedence [31:28] = 0  (TriduumSacrum — niveau 0 inclut Pâques, cf. NALC 1969)
            Nature     [27:25] = 0  (Solemnitas)
            Color      [24:22] = 0  (Albus)
            Season     [21:19] = 5  (TempusPaschale)
            Reserved   [18]    = 0
            FeastID    [17:0]  = 0x00001

[Année 2025, Jour 366 (padding année non-bissextile)]
000005C4: FF FF FF FF                                       |....|
          └───┬───┘
            0xFFFFFFFF (DayPacked::invalid())
            Precedence [31:28] = 15 → hors domaine (max=12), rejeté
            Nature     [27:25] = 7  → hors domaine (max=4),  rejeté
```

---

## 13. Résumé des Corrections et Hardening

| #   | Risque Identifié              | Correction                              | Criticité   | Section |
| --- | ----------------------------- | --------------------------------------- | ----------- | ------- |
| 1   | Collisions FeastID            | Registry canonique + import/export      | **Moyenne** | 3.1-3.3 |
| 2   | Séparation Logic/Packed       | Types distincts Logic/Packed            | **Haute**   | 1.1     |
| 3   | Précédence non-stricte        | PrecedenceResolver documenté            | **Moyenne** | 4.4     |
| 4   | HashMap non-déterministe      | BTreeMap partout dans Forge             | **Haute**   | 5.1     |
| 5   | Lifetime 'static non-justifié | Documentation invariants Mmap           | **Haute**   | 7.2     |
| 6   | Bornes années non-validées    | Validation 1583-4099 stricte            | **Moyenne** | 5.1     |
| 7   | Identité Forge/Runtime        | Test loop complet                       | **Haute**   | 11.4    |
| 8   | Versioning binaire            | Header flags + migration strategy       | **Haute**   | 8.1-8.2 |
| 9   | Corruptions silencieuses      | Telemetry + logs structurés             | **Haute**   | 9.1-9.2 |
| 10  | Endianness implicite          | Documentation + détection runtime       | **Moyenne** | 2.2     |
| 11  | FFI error reporting           | KalResult + last_error             | **Haute**   | 10.1    |
| 12  | Couverture tests              | Fuzzing + cross-build + interop         | **Haute**   | 11.5-11 |

---

## Annexe A : Architecture du Projet

### A.1 Structure Workspace

Le projet Liturgical Calendar est organisé en **workspace multi-crates Cargo** pour garantir la séparation des responsabilités et l'évolutivité à long terme.

**Workspace root** : `liturgical-calendar/`

**Crates** :

1. **`liturgical-calendar-core`** — Cœur algorithmique
   - Types de domaine canoniques (§1)
   - Algorithmes de calcul : easter, seasons, precedence (§4)
   - Slow Path complet
   - **Zéro dépendance externe** (critère clé pour WASM/embarqué)
   - Compatible `#![no_std]` (avec feature `std` optionnelle)

2. **`liturgical-calendar-io`** — Sérialisation binaire
   - Lecture/écriture format .kald (§2)
   - Provider de strings .lits (§6)
   - Validation stricte des formats
   - Gestion mmap et stratégies de chargement

3. **`liturgical-calendar-runtime`** — Runtime library
   - Provider Fast/Slow Path (§7)
   - Télémétrie et observabilité (§9)
   - Bindings FFI C/C++ (§10, feature optionnelle)
   - Gestion des corruptions et diagnostics

4. **`liturgical-calendar-forge`** — Build tool (binary)
   - CLI de génération de .kald (§5)
   - FeastID Registry (§3)
   - Configuration loader (§5.1)
   - Déterminisme garanti (BTreeMap)

5. **`kald-inspect`** — Diagnostic tool (binary)
   - Inspection de fichiers .kald (§8.2)
   - Détection de corruptions
   - Validation de formats et endianness
   - Reporting structuré

### A.2 Graphe de Dépendances

```
liturgical-calendar-core (0 dépendances externes)
          │
          ├─→ liturgical-calendar-io
          │         │
          │         └─→ liturgical-calendar-runtime
          │                   │
          │                   └─→ (runtime utilisateurs)
          │
          ├─→ liturgical-calendar-forge (binary)
          │
          └─→ kald-inspect (binary)
```

**Propriétés** :
- Graphe acyclique (pas de dépendances circulaires)
- Hiérarchie claire (core → io → runtime → tools)
- Dépendances minimales (chaque crate tire uniquement ce nécessaire)

### A.3 Structure de Fichiers

```
liturgical-calendar/
├── Cargo.toml                          # Workspace root
├── README.md
├── LICENSE
│
├── crates/
│   ├── liturgical-calendar-core/       # Crate 1
│   │   ├── Cargo.toml
│   │   ├── src/
│   │   │   ├── lib.rs
│   │   │   ├── types.rs
│   │   │   ├── easter.rs
│   │   │   ├── seasons.rs
│   │   │   ├── precedence.rs
│   │   │   └── error.rs
│   │   └── tests/
│   │
│   ├── liturgical-calendar-io/         # Crate 2
│   │   ├── Cargo.toml
│   │   ├── src/
│   │   │   ├── lib.rs
│   │   │   ├── litu/
│   │   │   │   ├── mod.rs
│   │   │   │   ├── header.rs
│   │   │   │   ├── reader.rs
│   │   │   │   └── writer.rs
│   │   │   └── lits/
│   │   │       ├── mod.rs
│   │   │       └── provider.rs
│   │   └── tests/
│   │
│   ├── liturgical-calendar-runtime/    # Crate 3
│   │   ├── Cargo.toml
│   │   ├── src/
│   │   │   ├── lib.rs
│   │   │   ├── provider.rs
│   │   │   ├── telemetry.rs
│   │   │   └── ffi.rs              # feature = "ffi"
│   │   └── tests/
│   │
│   ├── liturgical-calendar-forge/      # Crate 4
│   │   ├── Cargo.toml
│   │   ├── src/
│   │   │   ├── main.rs
│   │   │   ├── builder.rs
│   │   │   ├── registry.rs
│   │   │   └── config.rs
│   │   └── tests/
│   │
│   └── kald-inspect/                   # Crate 5
│       ├── Cargo.toml
│       └── src/
│           └── main.rs
│
├── examples/                           # Exemples d'usage
│   ├── basic_usage.rs
│   ├── custom_config.rs
│   └── ffi_example.c
│
└── docs/
    └── architecture.md
```

### A.4 Cargo.toml Workspace (Racine)

```toml
[workspace]
resolver = "2"
members = [
    "crates/liturgical-calendar-core",
    "crates/liturgical-calendar-io",
    "crates/liturgical-calendar-runtime",
    "crates/liturgical-calendar-forge",
    "crates/kald-inspect",
]

[workspace.package]
version = "0.1.0"
edition = "2021"
license = "MIT OR Apache-2.0"
authors = ["Liturgical Calendar Contributors"]
repository = "https://github.com/user/liturgical-calendar"

[workspace.dependencies]
# Dépendances partagées (versions unifiées)
memmap2 = "0.9"
serde = { version = "1.0", features = ["derive"] }
serde_json = "1.0"
clap = { version = "4.5", features = ["derive"] }

# Crates internes
liturgical-calendar-core = { path = "crates/liturgical-calendar-core" }
liturgical-calendar-io = { path = "crates/liturgical-calendar-io" }
liturgical-calendar-runtime = { path = "crates/liturgical-calendar-runtime" }
```

> **`thiserror` retiré des workspace.dependencies** : le crate `core` est `no_std` et ne peut pas utiliser `thiserror` (qui dépend de `std::error::Error`). Les crates `io`, `forge` et `runtime` peuvent l'adopter localement si souhaité, mais la hiérarchie définie en §9.1 est suffisamment simple pour s'en passer.

**Cargo.toml du crate `core` (no_std)**

```toml
# crates/liturgical-calendar-core/Cargo.toml
[package]
name = "liturgical-calendar-core"
version.workspace = true
edition.workspace = true

[features]
# Par défaut : no_std pur, zéro dépendance
default = []
# Activer pour exposer impl std::error::Error sur DomainError
std = []

[dependencies]
# Aucune dépendance externe — zéro transitive closure
```

```rust
// crates/liturgical-calendar-core/src/lib.rs
#![no_std]
// core::fmt est disponible sans std (fourni par le compilateur)

// Tout le code du crate n'accède qu'à core::*
// Pas de extern crate std, pas de alloc, pas de String, pas de Vec
```

### A.5 Principes Directeurs

**1. Core Stable**
- Le crate `core` a une API stable et minimaliste
- Versioning sémantique strict (1.x → 2.x rare)
- Rupture uniquement si les règles liturgiques canoniques changent

**2. Isolation I/O**
- La sérialisation est strictement séparée du calcul
- Formats binaires évolutifs sans impact sur le core
- Migration de formats documentée et testée

**3. Tools Optionnels**
- Les binaires CLI ne sont pas des dépendances obligatoires
- Installation séparée via `cargo install`
- Utilisables indépendamment du code

**4. FFI Feature**
- Les bindings C sont optionnels (feature flag)
- Activation : `liturgical-calendar-runtime = { features = ["ffi"] }`
- Isolation des dépendances système (libc)

**5. WASM-Ready**
- Le core compile en WebAssembly sans modification
- Zéro dépendance sur I/O système
- `#![no_std]` compatible

### A.6 Utilisation

**Calcul uniquement (Slow Path pur)** :
```toml
[dependencies]
liturgical-calendar-core = "0.1"
```

**Runtime complet (Fast/Slow Path + fichiers .kald)** :
```toml
[dependencies]
liturgical-calendar-runtime = "0.1"
```

**Avec bindings C/C++** :
```toml
[dependencies]
liturgical-calendar-runtime = { version = "0.1", features = ["ffi"] }
```

**Génération de .kald** :
```bash
$ cargo install liturgical-calendar-forge
$ liturgical-calendar-forge build --config france.toml --output france.kald
```

**Inspection de .kald** :
```bash
$ cargo install kald-inspect
$ kald-inspect france.kald --check
```

### A.7 Justification Multi-Crate

**Séparation des Responsabilités** :
- **Core** : Algorithmes purs, zéro I/O
- **I/O** : Formats binaires, mmap
- **Runtime** : Composition core + I/O + télémétrie
- **Tools** : CLI utilisateur final

**Évolutivité** :
- Ajout de nouveaux crates (serveur HTTP, export ICS) sans impact
- Feature flags limités (pas de matrice explosive)
- Dépendances ciblées (pas de bloat)

**Testabilité** :
- Tests du core : rapides, zéro I/O, déterministes
- Tests du runtime : avec fixtures .kald
- Isolation complète des suites de tests

**Contrôle des Dépendances** :
- Audit de sécurité facile (`cargo audit -p liturgical-calendar-core`)
- Core à zéro dépendance externe (auditable manuellement)
- Dépendances lourdes isolées (clap, serde dans tools uniquement)

**Coût Initial vs Long Terme** :
- Setup initial : +1 semaine
- Maintenance : -30% (compilation incrémentale, tests ciblés)
- Évolution : +50% facilité (ajout de features isolées)

### A.8 Séparation Moteur / Règles Liturgiques

**Principe** : L'architecture du projet doit strictement découpler le **moteur de calcul** de la **source des règles liturgiques**.

**Implémentation dans le Workspace** :

```
liturgical-calendar-core/
├── src/
│   ├── engine/              # Moteur de calcul (stable)
│   │   ├── slow_path.rs
│   │   ├── temporal.rs
│   │   └── sanctoral.rs
│   ├── rules/               # Structures déclaratives (stable)
│   │   ├── provider.rs      # Trait RuleProvider
│   │   ├── types.rs         # TemporalRule, SanctoralFeast
│   │   └── mod.rs
│   └── lib.rs

liturgical-calendar-rules-roman/  # Nouveau crate (v1.0)
├── src/
│   ├── hardcoded.rs         # HardcodedRuleProvider (~50 règles)
│   └── lib.rs

liturgical-calendar-rules-aot/    # Nouveau crate (v2.0+, optionnel)
├── src/
│   ├── generated.rs         # Code généré depuis YAML
│   └── lib.rs
```

**Graphe de Dépendances** :

```
liturgical-calendar-core
    │
    ├─→ liturgical-calendar-rules-roman (v1.0)
    │       └─→ implémente RuleProvider (hardcodé)
    │
    └─→ liturgical-calendar-rules-aot (v2.0+, optionnel)
            └─→ implémente RuleProvider (généré depuis YAML)
```

**Contrat** :

Le trait `RuleProvider` dans `core` garantit que le moteur ne dépend **jamais** d'une implémentation concrète :

```rust
// liturgical-calendar-core/src/rules/provider.rs
pub trait RuleProvider {
    fn temporal_rules(&self) -> &[TemporalRule];
    fn sanctoral_feasts(&self) -> &[SanctoralFeast];
    fn precedence_rules(&self) -> &PrecedenceRules;
}

// Le moteur accepte n'importe quelle implémentation
pub struct SlowPath {
    temporal: TemporalLayer,
    sanctoral: SanctoralLayer,
    precedence: PrecedenceResolver,
}

impl SlowPath {
    pub fn new<P: RuleProvider>(provider: P) -> Self {
        // Construit le moteur depuis les règles fournies
        Self {
            temporal: TemporalLayer::from_rules(provider.temporal_rules()),
            sanctoral: SanctoralLayer::from_feasts(provider.sanctoral_feasts()),
            precedence: PrecedenceResolver::from_rules(provider.precedence_rules()),
        }
    }
}
```

**Stratégie Évolutive** :

| Phase | Implémentation | Crate | Utilisateur |
|-------|----------------|-------|-------------|
| **v1.0** | Hardcodée Rust | `rules-roman` | Forge, tests |
| **v2.0+** | AOT depuis YAML | `rules-aot` | Production multi-rites |

**Avantages** :

1. **Stabilité du Core** : Le moteur ne change pas entre v1.0 et v2.0
2. **Testabilité** : Le moteur peut être testé avec des règles mock
3. **Extensibilité** : Nouveaux rites = nouveaux crates de règles
4. **Migration Progressive** : Passage hardcodé → AOT transparent

**Exemple d'Usage** :

```rust
// v1.0 : Hardcodé
use liturgical_calendar_core::SlowPath;
use liturgical_calendar_rules_roman::HardcodedRuleProvider;

let rules = HardcodedRuleProvider::new_roman_rite_ordinary();
let slow_path = SlowPath::new(rules);

// v2.0+ : AOT (même API !)
use liturgical_calendar_rules_aot::YamlAotRuleProvider;

let rules = YamlAotRuleProvider::new_roman_rite_ordinary();
let slow_path = SlowPath::new(rules);  // Code identique
```

**Critère de Conformité** :

Le code du moteur (`liturgical-calendar-core/src/engine/`) ne doit **jamais** contenir :
- De constantes liturgiques hardcodées (`const ASCENSION_OFFSET: i16 = 39`)
- De logique conditionnelle spécifique aux règles (`if feast_name == "Ascension"`)
- De dépendances sur des implémentations concrètes de règles

Toute la connaissance liturgique doit être dans les structures `TemporalRule` et `SanctoralFeast` fournies par le `RuleProvider`.

---

## Annexe B : Conventions de Nommage

### B.1 Principes Généraux

Le projet Liturgical Calendar respecte strictement les conventions Rust idiomatiques :

1. **`snake_case`** : Fonctions, variables, modules
   - Exemple : `compute_easter()`, `day_of_year`, `slow_path`

2. **`PascalCase`** : Types (structs, enums, traits)
   - Exemple : `Day`, `Header`, `Provider`

3. **`SCREAMING_SNAKE_CASE`** : Constantes
   - Exemple : `KNOWN_FLAGS_V1`, `HEADER_SIZE`

**Principe Fondamental** : Les modules portent le contexte, pas les noms de types.

### B.2 Vocabulaire du Domaine

#### B.2.1 Latin Canonique

Les enums du domaine liturgique utilisent le latin, vocabulaire canonique de l'Église Catholique Romaine :

```rust
pub enum Color {
    Albus,      // Blanc
    Rubeus,     // Rouge
    Viridis,    // Vert
    Violaceus,  // Violet
    Roseus,     // Rose
    Niger,      // Noir
}

pub enum Precedence {
    TriduumSacrum                     = 0,
    SollemnitatesFixaeMaior           = 1,
    // ... 13 valeurs, voir §1.3
}

pub enum Nature {
    Solemnitas,   // Solennité
    Festum,       // Fête
    Memoria,      // Mémoire
    Feria,        // Férie / Dimanche
    Commemoratio, // Commémoration
}

pub enum Season {
    TempusOrdinarium,   // Temps Ordinaire
    TempusAdventus,     // Avent
    TempusNativitatis,  // Temps de Noël
    TempusQuadragesimae,// Carême
    // ...
}
```

**Justification** :
- Évite les ambiguïtés de traduction (ex: "Ordinary Time" vs "Temps Ordinaire" vs "Tiempo Ordinario")
- Reste fidèle au vocabulaire liturgique officiel
- Facilite l'interopérabilité internationale

### B.3 Absence de Préfixes Redondants

**Antipattern : Préfixes C-like** ❌

```rust
// ❌ ÉVITER : Préfixes redondants
pub struct LiturgicalCalendarSlowPath { /* ... */ }
pub struct LiturgicalCalendarProvider { /* ... */ }
pub fn liturgical_calendar_compute_easter(year: i32) -> u16 { /* ... */ }
```

**Pattern Idiomatique : Contexte via Modules** ✅

```rust
// ✅ PRÉFÉRER : Modules portent le contexte
pub struct SlowPath { /* ... */ }
pub struct Provider { /* ... */ }
pub fn compute_easter(year: i32) -> u16 { /* ... */ }

// Usage avec chemin complet
use liturgical_calendar_core::SlowPath;
use liturgical_calendar_runtime::Provider;

let slow_path = SlowPath::new(rules);
let provider = Provider::new("data.kald", "lang.lits")?;
```

**Justification** :
- Le nom du crate (`liturgical_calendar_core`) fournit déjà le contexte complet
- Les types restent concis et lisibles
- Pattern standard Rust (cf. `std::io::Error` pas `std::io::StdIoError`)

### B.4 FFI C : Préfixe `kal_`

**Contexte** : Le C n'a pas de namespaces. Un préfixe est nécessaire pour éviter les collisions dans l'espace de noms global.

**Choix** : `kal_` (abréviation de "Kalendarium", l'artefact AOT produit par le moteur)

```c
// kal.h
// Note: The "kal_" prefix derives from "Kalendarium", the compiled annual
//       artifact produced by this engine. It keeps function names concise
//       while avoiding namespace collisions in C.

typedef struct KalProvider KalProvider;

KalProvider* kal_new(const char* data_path, const char* lang_path);
void kal_free(KalProvider* handle);
uint32_t kal_get_day(const KalProvider* h, int16_t year, uint16_t day);
```

**Justification** :
- Plus court que `liturgical_calendar_*` (24 caractères)
- Cohérent avec des pratiques courantes :
  - libgit2 → `git_*`
  - libssh2 → `ssh2_*`  
  - libcurl → `curl_*`
- Dérive de "Kalendarium" (artefact AOT), terme officiel du projet
- Abréviation explicitement documentée

### B.5 Alias de Crates Recommandés

Pour réduire la verbosité des imports dans le code utilisateur :

```rust
// Sans alias (verbeux)
use liturgical_calendar_core::SlowPath;
use liturgical_calendar_core::types::Day;
use liturgical_calendar_runtime::Provider;
use liturgical_calendar_rules_roman::HardcodedRuleProvider;

// Avec alias (recommandé)
use liturgical_calendar_core as core;
use liturgical_calendar_runtime as runtime;
use liturgical_calendar_rules_roman as rules;

let provider = rules::HardcodedRuleProvider::new();
let slow_path = core::SlowPath::new(provider);
let runtime_provider = runtime::Provider::new("data.kald", "lang.lits")?;
```

**Alternative : Crate "Façade" (Optionnel)**

Un crate racine peut réexporter les APIs principales :

```rust
// liturgical-calendar/src/lib.rs (crate façade optionnel)
pub use liturgical_calendar_core::*;
pub use liturgical_calendar_runtime::Provider;

// Usage simplifié
use liturgical_calendar::SlowPath;
use liturgical_calendar::Provider;
```

### B.6 Variables et Noms Locaux

**Principe** : Le type et le contexte suffisent, pas besoin de répéter l'information.

**Antipattern** ❌ :
```rust
let liturgical_day_logic = provider.get_day(2025, 1);
let liturgical_season_boundaries = SeasonBoundaries::compute(2025)?;
```

**Pattern Idiomatique** ✅ :
```rust
let day = provider.get_day(2025, 1);
let boundaries = SeasonBoundaries::compute(2025)?;
```

**Justification** : Le type est déjà explicite via l'annotation ou l'inférence.

### B.7 Noms de Modules

**Structure Recommandée** :

```
liturgical-calendar-core/src/
├── types.rs           // Types de domaine
├── easter.rs          // Algorithmes Pâques
├── seasons.rs         // Frontières temporelles
├── rules/
│   ├── mod.rs
│   ├── provider.rs    // Trait RuleProvider
│   └── types.rs       // TemporalRule, SanctoralFeast
└── engine/
    ├── mod.rs
    ├── slow_path.rs
    ├── temporal.rs
    └── sanctoral.rs
```

**Imports** :
```rust
use liturgical_calendar_core::types::Day;
use liturgical_calendar_core::rules::RuleProvider;
use liturgical_calendar_core::engine::SlowPath;
```

### B.8 Cohérence Terminologique

**Principe** : Utiliser un vocabulaire consistant dans tout le projet.

| Concept | Terme Utilisé | Éviter |
|---------|---------------|--------|
| Fournisseur de données | `Provider` | `Supplier`, `Source` |
| Règles liturgiques | `Rule` | `Policy`, `Regulation` |
| Couche de calcul | `Layer` | `Level`, `Tier` |
| Chemin de calcul | `Path` (Fast/Slow) | `Route`, `Mode` |
| Fête liturgique | `Feast` | `Holiday`, `Celebration` |

### B.9 Exemples Conformes

**✅ Excellent** :
```rust
pub struct Provider { /* ... */ }
pub fn compute_easter(year: i32) -> Option<(u8, u8)> { /* ... */ }
pub enum Color { Albus, Rubeus, /* ... */ }
const KNOWN_FLAGS_V1: u16 = 0x0000;
```

**❌ À Éviter** :
```rust
pub struct LiturgicalCalendarRuntimeProvider { /* ... */ }  // Redondant
pub fn LiturgicalComputeEaster(year: i32) -> (u8, u8) { /* ... */ }  // PascalCase fonction
pub enum Color { White, Red, /* ... */ }  // Perd le contexte latin
const KnownFlagsV1: u16 = 0x0000;  // Pas SCREAMING_SNAKE_CASE
```

### B.10 Checklist de Conformité

Avant l'implémentation, vérifier :

- [ ] Aucun préfixe `liturgical_calendar_*` dans les noms de types Rust
- [ ] FFI utilise `kal_*` avec note explicative
- [ ] Enums liturgiques en latin (sauf justification)
- [ ] `Rank` absent du codebase — modèle 2D uniquement (`Precedence` + `Nature`)
- [ ] `snake_case` pour fonctions et variables
- [ ] `PascalCase` pour types et enums
- [ ] `SCREAMING_SNAKE_CASE` pour constantes
- [ ] Modules utilisés pour porter le contexte
- [ ] Variables locales concises (pas de répétition du type)

---

**Fin de la Spécification Technique v2.0**

_Document consolidé le 2026-02-27. Intègre la transition vers le modèle 2D découplé (Precedence + Nature), le layout DayPacked v2.0, et le freeze des invariants structurels. Version de référence pour l'implémentation._
