import re


class Base:

    def match(self, message):
        return re.search(self.pattern, message)

    def process(self, match):
        raise NotImplementedError
