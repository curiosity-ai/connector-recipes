-- Seed for the grants demo. Works on both PostgreSQL and MySQL with minor
-- type tweaks (TIMESTAMP/DATETIME). PostgreSQL syntax below.

CREATE TABLE IF NOT EXISTS grants (
    id              VARCHAR(32) PRIMARY KEY,
    title           TEXT        NOT NULL,
    amount_usd      BIGINT      NOT NULL,
    start_year      INT         NOT NULL,
    end_year        INT         NOT NULL,
    status          VARCHAR(32) NOT NULL,
    awarded_at      TIMESTAMP   NOT NULL,
    updated_at      TIMESTAMP   NOT NULL,
    pi_email        VARCHAR(128) NOT NULL,
    pi_name         VARCHAR(128) NOT NULL,
    university      VARCHAR(128) NOT NULL,
    research_area   VARCHAR(128) NOT NULL,
    agency_acronym  VARCHAR(32)  NOT NULL,
    agency_name     VARCHAR(128) NOT NULL,
    agency_country  VARCHAR(64)  NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_grants_updated_at ON grants(updated_at);

INSERT INTO grants VALUES
 ('G-2024-001', 'Foundations of Robust Deep Learning',           1850000, 2024, 2027, 'Active',    '2024-02-14', '2024-02-14 09:00:00', 'h.ortega@cmu.edu',     'Hugo Ortega',     'CMU',       'Machine Learning',     'NSF',  'National Science Foundation', 'USA'),
 ('G-2024-002', 'Quantum Materials for Spintronic Devices',      2400000, 2024, 2028, 'Active',    '2024-03-01', '2024-03-02 11:30:00', 'v.singh@stanford.edu', 'Vikram Singh',    'Stanford',  'Quantum Mechanics',    'DOE',  'Department of Energy',         'USA'),
 ('G-2024-003', 'Predictive Models for Climate Anomalies',       1300000, 2024, 2026, 'Active',    '2024-04-15', '2024-04-15 14:00:00', 'm.lin@berkeley.edu',   'Mei Lin',         'Berkeley',  'Statistics',           'NSF',  'National Science Foundation', 'USA'),
 ('G-2023-051', 'Algorithms for Distributed Consensus at Scale',  950000, 2023, 2025, 'Active',    '2023-07-22', '2024-05-09 16:45:00', 't.ng@mit.edu',         'Terrence Ng',     'MIT',       'Distributed Systems',  'NSF',  'National Science Foundation', 'USA'),
 ('G-2024-007', 'CRISPR Tooling for Plant Pathogen Resistance',  1750000, 2024, 2027, 'Active',    '2024-05-20', '2024-05-20 09:15:00', 'e.patel@stanford.edu', 'Esha Patel',      'Stanford',  'Molecular Biology',    'NIH',  'National Institutes of Health','USA'),
 ('G-2024-011', 'Logic Programming for Verified Compilers',       620000, 2024, 2026, 'Active',    '2024-06-03', '2024-06-04 08:00:00', 'n.davies@oxford.ac.uk','Nigel Davies',    'Oxford',    'Logic',                'UKRI', 'UK Research and Innovation',  'UK'),
 ('G-2023-117', 'Microeconomic Effects of AI Adoption',           780000, 2023, 2025, 'Closed',    '2023-09-12', '2024-06-21 12:30:00', 'g.hartl@berkeley.edu', 'Greta Hartl',     'Berkeley',  'Microeconomics',       'NSF',  'National Science Foundation', 'USA'),
 ('G-2024-018', 'Tensor Network Methods for Many-Body Physics',  2100000, 2024, 2028, 'Active',    '2024-07-08', '2024-07-08 13:00:00', 'v.singh@stanford.edu', 'Vikram Singh',    'Stanford',  'Quantum Mechanics',    'DOE',  'Department of Energy',         'USA'),
 ('G-2024-024', 'Faster Linear Algebra for Sparse Systems',       560000, 2024, 2026, 'Active',    '2024-08-19', '2024-08-19 10:00:00', 'r.fields@mit.edu',     'Renee Fields',    'MIT',       'Linear Algebra',       'NSF',  'National Science Foundation', 'USA'),
 ('G-2024-030', 'Foundation Models for Computational Biology',   3000000, 2024, 2029, 'Active',    '2024-09-05', '2024-09-05 15:30:00', 'h.ortega@cmu.edu',     'Hugo Ortega',     'CMU',       'Machine Learning',     'NIH',  'National Institutes of Health','USA');
