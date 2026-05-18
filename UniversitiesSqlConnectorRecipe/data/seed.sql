-- Seed data for the SQL connector recipe.
-- The connector creates universities.db from this file on first run.

PRAGMA foreign_keys = ON;

DROP TABLE IF EXISTS faculty_research_areas;
DROP TABLE IF EXISTS department_research_areas;
DROP TABLE IF EXISTS faculty;
DROP TABLE IF EXISTS programs;
DROP TABLE IF EXISTS departments;
DROP TABLE IF EXISTS universities;

CREATE TABLE universities (
    name           TEXT    PRIMARY KEY,
    country        TEXT    NOT NULL,
    founded_year   INTEGER,
    ranking        INTEGER,
    students_count INTEGER,
    website        TEXT
);

CREATE TABLE departments (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    university_name TEXT    NOT NULL REFERENCES universities(name),
    name            TEXT    NOT NULL,
    building        TEXT,
    head_name       TEXT,
    head_email      TEXT,
    UNIQUE (university_name, name)
);

CREATE TABLE programs (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    department_id  INTEGER NOT NULL REFERENCES departments(id),
    name           TEXT    NOT NULL,
    degree_level   TEXT    NOT NULL,
    duration_years INTEGER NOT NULL,
    language       TEXT,
    tuition_usd    INTEGER
);

CREATE TABLE faculty (
    email         TEXT    PRIMARY KEY,
    department_id INTEGER NOT NULL REFERENCES departments(id),
    name          TEXT    NOT NULL,
    title         TEXT,
    h_index       INTEGER,
    joined_year   INTEGER
);

CREATE TABLE department_research_areas (
    department_id INTEGER NOT NULL REFERENCES departments(id),
    research_area TEXT    NOT NULL,
    PRIMARY KEY (department_id, research_area)
);

CREATE TABLE faculty_research_areas (
    email         TEXT NOT NULL REFERENCES faculty(email),
    research_area TEXT NOT NULL,
    PRIMARY KEY (email, research_area)
);

-- ---------- Universities ----------
INSERT INTO universities (name, country, founded_year, ranking, students_count, website) VALUES
('MIT',                     'USA',         1861,  2, 11500, 'https://mit.edu'),
('Stanford',                'USA',         1885,  3, 17500, 'https://stanford.edu'),
('ETH Zurich',              'Switzerland', 1855,  7, 22000, 'https://ethz.ch'),
('University of Tokyo',     'Japan',       1877, 28, 28000, 'https://u-tokyo.ac.jp'),
('University of Sao Paulo', 'Brazil',      1934, 85, 95000, 'https://usp.br'),
('Cambridge',               'UK',          1209,  5, 24000, 'https://cam.ac.uk'),
('Oxford',                  'UK',          1096,  4, 26000, 'https://ox.ac.uk');

-- ---------- Departments ----------
INSERT INTO departments (university_name, name, building, head_name, head_email) VALUES
('MIT',                     'Computer Science',       'Stata Center',     'Marcus Hill',  'mhill@mit.edu'),
('MIT',                     'Mathematics',            'Building 2',       'Sara Patel',   'spatel@mit.edu'),
('MIT',                     'Electrical Engineering', 'Building 38',      'Rita Sanchez', 'rsanchez@mit.edu'),
('Stanford',                'Computer Science',       'Gates Building',   'Helen Yang',   'hyang@stanford.edu'),
('Stanford',                'Electrical Engineering', 'Packard Building', 'Robert Kim',   'rkim@stanford.edu'),
('ETH Zurich',              'Computer Science',       'CAB',              'Anke Becker',  'abecker@ethz.ch'),
('ETH Zurich',              'Mathematics',            'HG',               'Felix Ziegler','fziegler@ethz.ch'),
('University of Tokyo',     'Computer Science',       'Eng. Bldg. 6',     'Kenji Sato',   'ksato@u-tokyo.ac.jp'),
('University of Sao Paulo', 'Computer Science',       'IME',              'Luis Pereira', 'lpereira@usp.br'),
('Cambridge',               'Computer Science',       'William Gates',    'Jane Hutton',  'jhutton@cam.ac.uk'),
('Oxford',                  'Computer Science',       'Wolfson Building', 'Niall Rogers', 'nrogers@ox.ac.uk');

-- ---------- Programs ----------
INSERT INTO programs (department_id, name, degree_level, duration_years, language, tuition_usd) VALUES
((SELECT id FROM departments WHERE university_name='MIT' AND name='Computer Science'), 'BSc Computer Science',          'BSc', 4, 'English',  57000),
((SELECT id FROM departments WHERE university_name='MIT' AND name='Computer Science'), 'MSc Artificial Intelligence',   'MSc', 2, 'English',  60000),
((SELECT id FROM departments WHERE university_name='MIT' AND name='Mathematics'),      'MSc Applied Mathematics',       'MSc', 2, 'English',  56000),
((SELECT id FROM departments WHERE university_name='Stanford' AND name='Computer Science'),       'BSc Computer Science',           'BSc', 4, 'English',  61000),
((SELECT id FROM departments WHERE university_name='Stanford' AND name='Electrical Engineering'), 'BSc Computer Engineering',       'BSc', 4, 'English',  61000),
((SELECT id FROM departments WHERE university_name='ETH Zurich' AND name='Computer Science'), 'BSc Informatik',                  'BSc', 3, 'German',    1500),
((SELECT id FROM departments WHERE university_name='ETH Zurich' AND name='Mathematics'),      'MSc Statistics',                  'MSc', 2, 'English',   1500),
((SELECT id FROM departments WHERE university_name='University of Tokyo' AND name='Computer Science'), 'BSc Computer Science',     'BSc', 4, 'Japanese',  5000),
((SELECT id FROM departments WHERE university_name='University of Sao Paulo' AND name='Computer Science'), 'BSc Data Science',     'BSc', 4, 'Portuguese',  500),
((SELECT id FROM departments WHERE university_name='Cambridge' AND name='Computer Science'), 'BSc Computer Science',              'BSc', 3, 'English',  35000),
((SELECT id FROM departments WHERE university_name='Oxford'    AND name='Computer Science'), 'PhD Computer Science',              'PhD', 4, 'English',  38000);

-- ---------- Faculty ----------
INSERT INTO faculty (email, department_id, name, title, h_index, joined_year) VALUES
('mhill@mit.edu',         (SELECT id FROM departments WHERE university_name='MIT' AND name='Computer Science'),       'Marcus Hill',    'Professor',           62, 2005),
('spatel@mit.edu',        (SELECT id FROM departments WHERE university_name='MIT' AND name='Mathematics'),            'Sara Patel',     'Associate Professor', 41, 2012),
('rsanchez@mit.edu',      (SELECT id FROM departments WHERE university_name='MIT' AND name='Electrical Engineering'), 'Rita Sanchez',   'Professor',           55, 2007),
('hyang@stanford.edu',    (SELECT id FROM departments WHERE university_name='Stanford' AND name='Computer Science'),  'Helen Yang',     'Professor',           70, 2003),
('rkim@stanford.edu',     (SELECT id FROM departments WHERE university_name='Stanford' AND name='Electrical Engineering'), 'Robert Kim','Associate Professor', 38, 2014),
('abecker@ethz.ch',       (SELECT id FROM departments WHERE university_name='ETH Zurich' AND name='Computer Science'), 'Anke Becker',   'Professor',           52, 2009),
('fziegler@ethz.ch',      (SELECT id FROM departments WHERE university_name='ETH Zurich' AND name='Mathematics'),      'Felix Ziegler', 'Professor',           48, 2011),
('ksato@u-tokyo.ac.jp',   (SELECT id FROM departments WHERE university_name='University of Tokyo' AND name='Computer Science'), 'Kenji Sato',    'Professor',           45, 2008),
('lpereira@usp.br',       (SELECT id FROM departments WHERE university_name='University of Sao Paulo' AND name='Computer Science'), 'Luis Pereira','Associate Professor', 30, 2015),
('jhutton@cam.ac.uk',     (SELECT id FROM departments WHERE university_name='Cambridge' AND name='Computer Science'), 'Jane Hutton',   'Professor',           68, 1998),
('nrogers@ox.ac.uk',      (SELECT id FROM departments WHERE university_name='Oxford' AND name='Computer Science'),    'Niall Rogers',  'Professor',           60, 2002);

-- ---------- Department research areas ----------
INSERT INTO department_research_areas (department_id, research_area) VALUES
((SELECT id FROM departments WHERE university_name='MIT' AND name='Computer Science'),                'Artificial Intelligence'),
((SELECT id FROM departments WHERE university_name='MIT' AND name='Computer Science'),                'Distributed Systems'),
((SELECT id FROM departments WHERE university_name='MIT' AND name='Mathematics'),                     'Number Theory'),
((SELECT id FROM departments WHERE university_name='Stanford' AND name='Computer Science'),           'Artificial Intelligence'),
((SELECT id FROM departments WHERE university_name='Stanford' AND name='Computer Science'),           'Computer Vision'),
((SELECT id FROM departments WHERE university_name='Stanford' AND name='Electrical Engineering'),     'Signal Processing'),
((SELECT id FROM departments WHERE university_name='ETH Zurich' AND name='Computer Science'),         'Distributed Systems'),
((SELECT id FROM departments WHERE university_name='ETH Zurich' AND name='Computer Science'),         'Cryptography'),
((SELECT id FROM departments WHERE university_name='ETH Zurich' AND name='Mathematics'),              'Statistics'),
((SELECT id FROM departments WHERE university_name='University of Tokyo' AND name='Computer Science'),'Artificial Intelligence'),
((SELECT id FROM departments WHERE university_name='University of Sao Paulo' AND name='Computer Science'), 'Data Science'),
((SELECT id FROM departments WHERE university_name='Cambridge' AND name='Computer Science'),          'Theoretical Computer Science'),
((SELECT id FROM departments WHERE university_name='Oxford' AND name='Computer Science'),             'Cryptography');

-- ---------- Faculty research areas ----------
INSERT INTO faculty_research_areas (email, research_area) VALUES
('mhill@mit.edu',       'Artificial Intelligence'),
('mhill@mit.edu',       'Distributed Systems'),
('spatel@mit.edu',      'Number Theory'),
('rsanchez@mit.edu',    'Signal Processing'),
('hyang@stanford.edu',  'Artificial Intelligence'),
('hyang@stanford.edu',  'Computer Vision'),
('rkim@stanford.edu',   'Signal Processing'),
('abecker@ethz.ch',     'Distributed Systems'),
('abecker@ethz.ch',     'Cryptography'),
('fziegler@ethz.ch',    'Statistics'),
('ksato@u-tokyo.ac.jp', 'Artificial Intelligence'),
('lpereira@usp.br',     'Data Science'),
('jhutton@cam.ac.uk',   'Theoretical Computer Science'),
('nrogers@ox.ac.uk',    'Cryptography');
