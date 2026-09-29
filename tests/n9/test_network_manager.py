"""Offline regression tests for the Python 2.6-compatible phone component."""
import os
import importlib.util
import pathlib
import sys
import tempfile
import types
import unittest
from unittest import mock

if sys.platform == 'win32':
    sys.modules.setdefault('fcntl', types.ModuleType('fcntl'))
path = pathlib.Path(__file__).resolve().parents[2] / 'src/ResurectPhone.Infrastructure.Windows/NokiaN9/Resources/network-manager.py'
spec = importlib.util.spec_from_file_location('network_manager', path)
network = importlib.util.module_from_spec(spec)
spec.loader.exec_module(network)


class FakeSettings:
    def __init__(self):
        self.values = {(3, 'wifi-a', 'autoconnect'): {'type': 4, 'value': False},
                       (2, None, 'auto_connect'): {'type': 5, 'value': [{'type': 1, 'value': 'GPRS'}]},
                       (2, None, 'search_interval'): {'type': 2, 'value': 300}}
        self.fail = False

    def require(self, identity):
        if identity != 'wifi-a': raise RuntimeError('not Wi-Fi')
        return {'id': identity}

    def read(self, context, identity, key):
        return self.values.get((context, identity, key))

    def write(self, context, identity, key, value):
        if self.fail and key == 'search_interval' and value['value'] == 60:
            raise RuntimeError('simulated write failure')
        self.values[(context, identity, key)] = value


class NetworkTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.config = str(pathlib.Path(self.tmp.name) / 'config.json')
        self.backup = str(pathlib.Path(self.tmp.name) / 'backup.json')
        self.patch = mock.patch.object(network, 'CONFIG', self.config)
        if sys.platform == 'win32':
            # The N9 uses POSIX rename (atomic replacement), unlike Windows rename.
            rename = mock.patch.object(network.os, 'rename', os.replace)
            rename.start(); self.addCleanup(rename.stop)
        self.patch.start()
        self.addCleanup(self.patch.stop)

    def test_reconnect_respects_radios_and_existing_wifi(self):
        profiles = [{'id': 'wifi-a', 'automatic': True}]
        self.assertTrue(network.can_retry(15, [], profiles, 'wifi-a'))
        for radios in [0, 1, 4, 10]:
            self.assertFalse(network.can_retry(radios, [], profiles, 'wifi-a'))
        for state in [1, 2, 15]:
            self.assertFalse(network.can_retry(15, [['', 0, '', 'GPRS', 0, [], '', 2]], profiles, 'missing'))
            active = ['', 0, '', network.WLAN, 0, [], '', state]
            self.assertFalse(network.can_retry(15, [active], profiles, 'wifi-a'))
        self.assertFalse(network.can_retry(15, [], [{'id': 'wifi-a', 'automatic': False}], 'wifi-a'))

    def test_force_keeps_mobile_policy_and_restores_exact_values(self):
        settings = FakeSettings(); original = dict(settings.values)
        network.automatic(settings, 'wifi-a', True, True, self.backup)
        self.assertEqual(['wifi-a'], network.read_json(self.config, {})['forced'])
        auto = settings.read(2, None, 'auto_connect')['value']
        self.assertEqual(['GPRS', network.WLAN], [item['value'] for item in auto])
        self.assertEqual(60, settings.read(2, None, 'search_interval')['value'])
        network.restore(settings, self.backup)
        self.assertEqual(original, settings.values)
        self.assertFalse(pathlib.Path(self.config).exists())

    def test_failure_rolls_back_already_written_values(self):
        settings = FakeSettings(); original = dict(settings.values); settings.fail = True
        with self.assertRaises(RuntimeError):
            network.automatic(settings, 'wifi-a', True, True, self.backup)
        self.assertEqual(original, settings.values)

    def test_manual_mode_removes_only_selected_forced_profile(self):
        network.atomic_json(self.config, {'forced': ['wifi-a', 'wifi-b']})
        settings = FakeSettings()
        network.automatic(settings, 'wifi-a', False, False, self.backup)
        self.assertEqual(['wifi-b'], network.read_json(self.config, {})['forced'])
        self.assertEqual(300, settings.read(2, None, 'search_interval')['value'])

    def test_network_identity_matches_uuid_or_ssid_not_mobile(self):
        profile = {'id': 'wifi-a', 'ssid': [65, 66]}
        state = ['', 0, '', network.WLAN, 0, list(b'wifi-a'), '', 2]
        self.assertTrue(network.same_network(profile, state))
        state[5] = [65, 66]; self.assertTrue(network.same_network(profile, state))
        state[3] = 'GPRS'; self.assertFalse(network.same_network(profile, state))

    def test_retry_delay_is_bounded(self):
        self.assertEqual(30, network.retry_delay(0))
        self.assertEqual(60, network.retry_delay(1))
        self.assertEqual(300, network.retry_delay(99))


if __name__ == '__main__': unittest.main()
