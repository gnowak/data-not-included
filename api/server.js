const express = require('express');
const cors = require('cors');
const fs = require('fs');
const path = require('path');
const os = require('os');

const app = express();
const PORT = process.env.PORT || 3000;

app.use(cors());
app.use(express.json());

// Path to where the ONI Mod dumps the data
const dataDumpPath = `${os.homedir()}/Documents/Klei/OxygenNotIncluded/DataDump`;

const checkFileExists = fs.existsSync;
const readJsonFile = fs.readFileSync;

// Helper to safely read buildings JSON data
function readBuildingsFile() {
    const filePath = `${dataDumpPath}/buildings.json`;
    if (checkFileExists(filePath)) {
        try {
            const data = readJsonFile(filePath, 'utf8');
            return JSON.parse(data);
        } catch (err) {
            console.error('Error parsing buildings.json:', err);
        }
    }
    return null;
}

// Helper to safely read elements JSON data
function readElementsFile() {
    const filePath = `${dataDumpPath}/elements.json`;
    if (checkFileExists(filePath)) {
        try {
            const data = readJsonFile(filePath, 'utf8');
            return JSON.parse(data);
        } catch (err) {
            console.error('Error parsing elements.json:', err);
        }
    }
    return null;
}

// Generic helper to safely read any JSON data dump file
function readDataFile(filename) {
    const filePath = path.join(dataDumpPath, filename);
    if (checkFileExists(filePath)) {
        try {
            const data = readJsonFile(filePath, 'utf8');
            return JSON.parse(data);
        } catch (err) {
            console.error(`Error parsing ${filename}:`, err);
        }
    }
    return null;
}

// Routes
app.get('/api/buildings', (req, res) => {
    const buildings = readBuildingsFile();
    if (buildings) {
        res.json(buildings);
    } else {
        res.status(404).json({ error: 'Buildings data not found. Please ensure the mod has dumped the data.' });
    }
});

app.get('/api/buildings/:id', (req, res) => {
    const buildings = readBuildingsFile();
    if (buildings) {
        const building = buildings.find(b => b.id === req.params.id);
        if (building) {
            res.json(building);
        } else {
            res.status(404).json({ error: 'Building not found.' });
        }
    } else {
        res.status(404).json({ error: 'Buildings data not found.' });
    }
});

app.get('/api/elements', (req, res) => {
    const elements = readElementsFile();
    if (elements) {
        res.json(elements);
    } else {
        res.status(404).json({ error: 'Elements data not found. Please ensure the mod has dumped the data.' });
    }
});

app.get('/api/elements/:id', (req, res) => {
    const elements = readElementsFile();
    if (elements) {
        // e.id might be a string (e.g. "Oxygen") so we check ignore case if needed
        const element = elements.find(e => e.id.toLowerCase() === req.params.id.toLowerCase());
        if (element) {
            res.json(element);
        } else {
            res.status(404).json({ error: 'Element not found.' });
        }
    } else {
        res.status(404).json({ error: 'Elements data not found.' });
    }
});

app.get('/api/recipes', (req, res) => {
    const recipes = readDataFile('recipes.json');
    if (recipes) {
        res.json(recipes);
    } else {
        res.status(404).json({ error: 'Recipes data not found.' });
    }
});

app.get('/api/personalities', (req, res) => {
    const personalities = readDataFile('personalities.json');
    if (personalities) {
        res.json(personalities);
    } else {
        res.status(404).json({ error: 'Personalities data not found.' });
    }
});

app.get('/api/critters', (req, res) => {
    const data = readDataFile('critters.json');
    if (data) res.json(data);
    else res.status(404).json({ error: 'Critters data not found.' });
});

app.get('/api/plants', (req, res) => {
    const data = readDataFile('plants.json');
    if (data) res.json(data);
    else res.status(404).json({ error: 'Plants data not found.' });
});

app.get('/api/geysers', (req, res) => {
    const data = readDataFile('geysers.json');
    if (data) res.json(data);
    else res.status(404).json({ error: 'Geysers data not found.' });
});

app.get('/api/foods', (req, res) => {
    const data = readDataFile('foods.json');
    if (data) res.json(data);
    else res.status(404).json({ error: 'Foods data not found.' });
});

app.get('/api/equipment', (req, res) => {
    const data = readDataFile('equipment.json');
    if (data) res.json(data);
    else res.status(404).json({ error: 'Equipment data not found.' });
});

app.get('/api/space_pois', (req, res) => {
    const data = readDataFile('space_pois.json');
    if (data) res.json(data);
    else res.status(404).json({ error: 'Space POIs data not found.' });
});

app.get('/api/health', (req, res) => {
    res.json({ status: 'ok', dataDumpPath });
});

app.listen(PORT, () => {
    console.log(`Data Not Included API server is running on http://localhost:${PORT}`);
    console.log(`Looking for data dumps in: ${dataDumpPath}`);
});
