# Ledger

[![CI](https://github.com/sma-ali/Ledger/actions/workflows/ci.yml/badge.svg)](https://github.com/sma-ali/Ledger/actions/workflows/ci.yml)

Plusieurs outils produisent le résultat quotidien d'une même salle de marché,
chacun dans son format et avec ses propres libellés, et leurs montants ne
tombent jamais exactement pareil. Ledger ingère ces sources, les ramène à un
modèle commun, rapproche les montants sur une clé métier et qualifie chaque
écart selon des règles explicites, pour qu'on sache lesquels méritent qu'on
les regarde.

```
  fo_20260918.csv  ─┐
  (format anglo)    │
                    ├─► normalisation ─► réconciliation ─► SQL Server
  bo_20260918.csv  ─┘   (modèle commun)   (par clé métier)   (+ restitution)
  (format français)
```

## Démarrage

```bash
docker compose up --build
```

Une seule commande : SQL Server démarre, le batch attend qu'il soit vraiment
prêt, crée son schéma, charge les deux fichiers d'exemple, réconcilie et
affiche le résultat.

Pour rejouer une autre date métier :

```bash
docker compose run --rm ledger --date 2026-09-18
```

## Ce que ça affiche

```
  LEDGER | run 1 | date métier 2026-09-18
==============================================================================

  CONSOLIDATION PAR DESK
  Desk           Dev  Source             Montant   Lignes
  -----------------------------------------------------
  PARIS-FX       EUR  BO               77,130.15        3
  PARIS-FX       EUR  FO               77,130.45        2
  PARIS-FX       USD  BO               15,000.00        1
  PARIS-FX       USD  FO               15,000.00        1
  PARIS-RATES    EUR  BO               79,250.00        2
  PARIS-RATES    EUR  FO               86,800.25        2

  RÉCONCILIATION
  Statut               Clés     Écart cumulé
  ------------------------------------------
  Matched                 2             0.00
  WithinTolerance         1             0.30
  Break                   1         1,500.00
  Missing                 2             0.00

  À INVESTIGUER
  Desk           Instrument     Dev  Statut            Écart   Détail
  --------------------------------------------------------------------------
  PARIS-RATES    DE0007164600   EUR  Break          1,500.00   BO 79000.0000  |  FO 77500.0000
  PARIS-RATES    FR0000120271   EUR  Missing            0.00   FO 9300.2500
  PARIS-RATES    FR0000121014   EUR  Missing            0.00   BO 250.0000

  LIGNES REJETÉES À L'INGESTION
  --------------------------------------------------------------------------
  FO   ligne 7     Desk « DESK-INCONNU » absent du référentiel de correspondance.
```

## Le problème, concrètement

Les deux sources décrivent les mêmes faits, avec cinq conventions différentes :

| | Front office | Comptabilité |
|---|---|---|
| Séparateur | `,` | `;` |
| Date | `2026-09-18` | `18/09/2026` |
| Décimale | `125340.55` | `125 340,55` |
| Desk | `FXD-PARIS` | `PARIS_FX_DESK` |
| Colonnes | `TradeDate,DeskCode,…` | `DATE_COMPTA;PORTEFEUILLE;…` |

La table `DeskMapping` traduit les libellés de desk vers un libellé canonique.
Sans elle, aucun rapprochement n'est possible : les deux sources parlent des
mêmes équipes sans employer les mêmes mots.

## Schéma de données

```
  Source ──────┬──── DeskMapping        référentiels
               │
  BatchRun ────┼──── PnlEntry           une exécution, ses lignes normalisées
               ├──── IngestionReject    les lignes refusées, brutes + motif
               │
               └──── ReconciliationResult ──── ReconciliationLeg
                     un résultat par clé          un montant par source
```

La clé métier de rapprochement est `(BusinessDate, CanonicalDesk, InstrumentId, Currency)`.

Le résultat est découpé en deux tables plutôt qu'aplati en colonnes
`MontantFO` / `MontantBO` : une troisième source s'ajoute alors sans toucher
ni au schéma ni aux requêtes.

Les montants sont en `DECIMAL(19,4)`, jamais en `FLOAT` - l'arrondi binaire
d'un flottant fabriquerait exactement les écarts que l'application doit
détecter.

## Comment c'est découpé

```
src/Ledger.Domain        modèle commun + moteur de réconciliation - zéro dépendance
src/Ledger.Ingestion     lecture des CSV hétérogènes              → Domain
src/Ledger.Persistence   SQL Server via Dapper                     → Domain
src/Ledger.Batch         orchestration, configuration, restitution → tout
```

`Ledger.Domain` ne référence rien, et le compilateur le garantit : la logique
métier ne peut pas se mettre à faire du SQL. C'est ce qui permet de tester le
moteur de réconciliation en quelques millisecondes, sans conteneur.

## Règles de réconciliation

| Statut | Règle |
|---|---|
| `Matched` | écart nul |
| `WithinTolerance` | écart inférieur ou égal à la tolérance de la devise |
| `Break` | au-delà de la tolérance : à investiguer |
| `Missing` | la clé n'est présente que dans une partie des sources |

L'écart vaut le plus grand montant moins le plus petit : toujours positif, et
la formule reste juste au-delà de deux sources. La borne de tolérance est
incluse. Les seuils vivent dans la configuration, par devise, et le moteur les
reçoit en paramètre sans savoir d'où ils viennent.

Plusieurs lignes d'une même source sont agrégées avant comparaison - le front
éclate parfois un montant que la compta consolide. Ce n'est pas un écart, mais
`EntryCount` le rend visible.

## Rejouabilité

Le batch efface ce qu'un run précédent a produit pour la même date métier avant
de recommencer. Relancer trois fois la même journée laisse exactement un run et
le même contenu en base, jamais des doublons - le comportement attendu d'un
traitement relancé après incident par un ordonnanceur.

Une ligne portant une autre date que celle du run est rejetée plutôt
qu'ingérée : c'est le symptôme d'un fichier livré en double ou décalé d'un jour.

Le code de sortie du processus vaut zéro quand le traitement s'est déroulé
correctement, même s'il a trouvé des écarts. Des écarts sont un résultat, pas
une panne ; l'exploitation ne doit être alertée que si le batch n'a pas pu
faire son travail.

## Tests

```bash
dotnet test
```

34 tests, sur la logique métier uniquement : la qualification des écarts et la
conversion des formats sources. Ni base ni fichier, donc aucun conteneur à
démarrer.

Un de ces tests a attrapé un vrai bug : avec `NumberStyles.Number`, un montant
`12,34` venant d'un fichier anglo-saxon était lu **1234** sans broncher, la
virgule étant le séparateur de milliers de la culture invariante.

## Dépendances

Cinq paquets, chacun pour une raison précise :

| Paquet | Pourquoi |
|---|---|
| `CsvHelper` | un découpage manuel casse au premier champ entre guillemets |
| `Dapper` | mappe un résultat SQL vers des objets, sans générer de SQL |
| `Microsoft.Data.SqlClient` | le pilote SQL Server officiel |
| `Microsoft.Extensions.Hosting` | configuration, journalisation, injection de dépendances |
| `xUnit` | les tests |

**Dapper plutôt qu'EF Core** : les requêtes sont écrites à la main, lisibles
telles quelles dans le dépôt, et les agrégations par desk sont naturelles en
SQL. Le schéma est géré par un script idempotent, ce qui correspond d'ailleurs
à la pratique en banque, où une application ne modifie pas le schéma toute
seule.

## Périmètre

Fait : ingestion de deux sources CSV, modèle commun, schéma SQL Server,
réconciliation avec écarts qualifiés et stockés, batch de bout en bout
rejouable, tests.

Pas fait, volontairement : API REST, interface de consultation, source API,
ordonnancement via Quartz.NET, historisation et rejeu d'un run antérieur.

## Licence

MIT.
