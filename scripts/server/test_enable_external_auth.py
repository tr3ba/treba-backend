import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('enable_external_auth', Path(__file__).with_name('enable-external-auth.py'))
configuration = importlib.util.module_from_spec(spec)
spec.loader.exec_module(configuration)


class EnableExternalAuthTests(unittest.TestCase):
    fixture = ('# retained comment\nOTHER_SETTING=fixture-unchanged\n'
               'ExternalAuth__Google__ClientId=fixture.apps.googleusercontent.com\n'
               'ExternalAuth__Google__ClientSecret=fixture-google-secret\n'
               'ExternalAuth__Facebook__AppId=123456789\n'
               'ExternalAuth__Facebook__AppSecret=fixture-facebook-secret\n'
               'ExternalAuth__Google__Enabled=false\nExternalAuth__Facebook__Enabled=false\n')

    def test_enables_only_provider_flags_preserves_credentials_and_keeps_backup(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / 'backend.env'
            path.write_text(self.fixture, encoding='utf-8')
            self.assertTrue(configuration.enable(path))
            updated = path.read_text(encoding='utf-8')
            self.assertIn('ExternalAuth__Google__Enabled=true', updated)
            self.assertIn('ExternalAuth__Facebook__Enabled=true', updated)
            self.assertIn('OTHER_SETTING=fixture-unchanged', updated)
            self.assertIn('ExternalAuth__Google__ClientSecret=fixture-google-secret', updated)
            backups = list(Path(root).glob('backend.env.before-external-auth-*'))
            self.assertEqual(len(backups), 1)
            self.assertEqual(backups[0].read_text(encoding='utf-8'), self.fixture)
            self.assertFalse(configuration.enable(path))
            self.assertEqual(len(list(Path(root).glob('backend.env.before-external-auth-*'))), 1)

    def test_missing_or_invalid_credentials_leave_original_untouched(self):
        for fixture in [self.fixture.replace('fixture-google-secret', ''), self.fixture.replace('123456789', 'YOUR_APP_ID'), self.fixture.replace('fixture.apps.googleusercontent.com', 'invalid-client')]:
            with self.subTest(fixture=fixture), tempfile.TemporaryDirectory() as root:
                path = Path(root) / 'backend.env'
                path.write_text(fixture, encoding='utf-8')
                with self.assertRaises(ValueError):
                    configuration.enable(path)
                self.assertEqual(path.read_text(encoding='utf-8'), fixture)
                self.assertEqual(len(list(Path(root).iterdir())), 1)


if __name__ == '__main__':
    unittest.main()
